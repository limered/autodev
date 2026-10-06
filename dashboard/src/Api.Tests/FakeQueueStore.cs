using Api.Issues;
using Api.Queue;

namespace Api.Tests;

public sealed class FakeQueueStore : IQueueStore
{
    private readonly List<QueueRow> _items = new();
    private long _nextId = 1;

    /// <summary>
    /// The issues-domain seam the real store claims through. Unwired, ClaimNext keeps
    /// its legacy hardcoded payload; wired (e.g. to a <c>FakeIssueResolver</c>), the
    /// claim resolves through it and a missing issue row claims nothing — same as SQL.
    /// </summary>
    public IIssueResolver? Resolver { get; set; }

    public Catalogs.ITargetCatalogService? Catalogs { get; set; }

    /// <summary>The factory catalog the fake verifies fallback picks against; unwired answers no names.</summary>
    public Catalogs.IFactoryWorkflows? Factory { get; set; }

    public FakeIssuesStore? Issues { get; set; }

    /// <summary>
    /// The boundary projection: QueueRules sees only the queue row fields, so the
    /// enrichments the raw joined row carries from the LEFT JOIN are dropped here.
    /// </summary>
    private IEnumerable<QueueRuleItem> RuleItems() =>
        _items.Select(i => new QueueRuleItem(i.Id, i.IssueId, i.Rank, i.RunId, i.StartRequestedAt, i.Workflow));

    /// <summary>The queue row attached to a run, or none — the run's slot.</summary>
    public QueueRow? FindByRunId(Guid runId) => _items.FirstOrDefault(i => i.RunId == runId);

    /// <summary>
    /// Removes the queue row attached to a run, mirroring the resolver's
    /// <c>DELETE FROM queue WHERE run_id = @runId</c>.
    /// </summary>
    public void DeleteByRunId(Guid runId)
    {
        var slot = FindByRunId(runId);
        if (slot is not null)
        {
            _items.Remove(slot);
        }
    }

    public Task<IReadOnlyList<QueueRow>> All()
    {
        var ordered = _items.OrderBy(i => i.Rank).ToList();
        return Task.FromResult<IReadOnlyList<QueueRow>>(ordered);
    }

    public Task<QueueRow?> Enqueue(long issueId, string? workflow = null)
    {
        // Rank/dedup derivation is QueueRules', shared with the real SQL store; only
        // the storage here is fake.
        var existing = QueueRules.ExistingForIssue(RuleItems(), issueId);
        if (existing is not null)
        {
            return Task.FromResult<QueueRow?>(_items.Single(i => i.Id == existing.Id));
        }

        // The raw joined-row record for a queue row with no matching issues/runs
        // rows: the LEFT JOIN enrichments are null and the row is not present.
        var item = new QueueRow(
            _nextId++,
            issueId,
            QueueRules.NextRank(_items.Select(i => i.Rank)),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            Workflow: workflow);

        _items.Add(item);
        return Task.FromResult<QueueRow?>(item);
    }

    /// <summary>
    /// Sets the row's workflow pick while it is unclaimed (mirror of the SQL store's
    /// guarded update): a claimed row answers unchanged so the caller re-syncs and
    /// sees the frozen pick.
    /// </summary>
    public Task<QueueRow?> SetWorkflow(long id, string? workflow)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.RunId is not null)
        {
            return Task.FromResult(item);
        }

        var idx = _items.IndexOf(item);
        _items[idx] = item with { Workflow = workflow };
        return Task.FromResult<QueueRow?>(_items[idx]);
    }

    public async Task<QueueRow?> StartNext(long id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return null;
        }

        if (Catalogs is not null && Issues is not null)
        {
            var issue = (await Issues.All()).FirstOrDefault(i => i.GitHubId == item.IssueId);
            if (issue is not null)
            {
                await Catalogs.RevalidateAsync(issue.Repo);
            }
        }

        // QueueRules.ShouldRequestStart, shared with the real SQL store's
        // WHERE start_requested_at IS NULL: a repeat call keeps the first timestamp.
        if (!QueueRules.ShouldRequestStart(RuleItems().Single(i => i.Id == id)))
        {
            return item;
        }

        var idx = _items.IndexOf(item);
        _items[idx] = item with { StartRequestedAt = DateTimeOffset.UtcNow };
        return _items[idx];
    }

    public Task<QueueRow?> Restart(long id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return Task.FromResult<QueueRow?>(null);
        }

        var idx = _items.IndexOf(item);
        _items[idx] = item with { RunId = null, StartRequestedAt = null, ResumeBranch = null, ResumeStage = null, ParentRunId = null };
        return Task.FromResult<QueueRow?>(_items[idx]);
    }

    public Task<QueueRow?> PrepareResume(Guid oldRunId, string branch, string? resumeStage, Guid parentRunId)
    {
        var item = _items.FirstOrDefault(i => i.RunId == oldRunId);
        if (item is null)
        {
            return Task.FromResult<QueueRow?>(null);
        }

        var idx = _items.IndexOf(item);
        _items[idx] = item with
        {
            RunId = null,
            StartRequestedAt = DateTimeOffset.UtcNow,
            ResumeBranch = branch,
            ResumeStage = resumeStage,
            ParentRunId = parentRunId,
        };
        return Task.FromResult<QueueRow?>(_items[idx]);
    }

    public async Task<ClaimedQueueItem?> ClaimNext()
    {
        var next = QueueRules.NextClaimable(RuleItems());

        if (next is null)
        {
            return null;
        }

        // The claim payload comes from the issues domain via the same resolver seam
        // the SQL store claims through — a missing issue row answers null and claims
        // nothing, attaching no run, exactly like the SQL inner-join guarantee. The
        // fake ignores the caller's connection/transaction; that is its contract.
        var payload = Resolver is null
            ? new IssueClaimPayload("https://github.com/test/repo.git", "spec")
            : await Resolver.ResolveClaimPayloadAsync(next.IssueId, conn: null!, tx: null!);

        if (payload is null)
        {
            return null;
        }

        Catalogs.TargetCatalog? catalog = null;
        if (Catalogs is not null && Issues is not null)
        {
            var issue = (await Issues.All()).FirstOrDefault(i => i.GitHubId == next.IssueId);
            if (issue is not null)
            {
                catalog = await Catalogs.RevalidateAsync(issue.Repo);
            }
        }

        var (defaultName, names) = Api.Catalogs.CatalogRules.WorkflowNames(catalog, Factory);
        var decision = QueueRules.DecidePick(next.Workflow, defaultName, names);
        if (decision.Skip)
        {
            // Stale pick: skip the row — unclaimed with no run, retryable after the
            // catalog resyncs; shared with the real store's claim path.
            return null;
        }

        var runId = Guid.NewGuid();
        var item = _items.Single(i => i.Id == next.Id);
        var idx = _items.IndexOf(item);
        var branch = item.ResumeBranch;
        var resumeStage = item.ResumeStage;
        var parentRunId = item.ParentRunId;
        _items[idx] = item with { RunId = runId, ResumeBranch = null, ResumeStage = null, ParentRunId = null };
        return new ClaimedQueueItem(
            runId,
            payload.RepoUrl,
            payload.Spec,
            catalog?.Sha,
            catalog?.Source ?? Api.Catalogs.CatalogRules.SourceFactoryFallback,
            catalog?.Content,
            branch,
            resumeStage,
            parentRunId,
            next.Id,
            decision.Resolved);
    }

    public Task Reorder(IReadOnlyList<long> ids)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            var item = _items.FirstOrDefault(q => q.Id == ids[i]);
            if (item is not null)
            {
                var idx = _items.IndexOf(item);
                _items[idx] = item with { Rank = i + 1 };
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> Delete(long id)
    {
        var item = _items.FirstOrDefault(q => q.Id == id);
        if (item is null)
        {
            return Task.FromResult(false);
        }

        _items.Remove(item);
        return Task.FromResult(true);
    }
}
