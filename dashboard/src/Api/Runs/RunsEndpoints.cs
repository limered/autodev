namespace Api.Runs;

public static class RunsEndpoints
{
    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/runs/{runId:guid}/events", async (Guid runId, RunEvent? ev, IRunStore store) =>
        {
            // A JSON null body binds to null. Anything the wire mapper cannot type — an
            // unknown or missing discriminator — binds to a bare RunEvent, which the fold
            // no-ops, so producers never see a 500 for an event type this API doesn't know.
            if (ev is null)
            {
                return Results.BadRequest();
            }

            await store.Apply(runId, ev);
            return Results.Accepted();
        }).AddEndpointFilter<RequireFactoryToken>();

        app.MapGet("/runs", async (IRunStore store, int? skip, int? take) =>
        {
            // Backward-compatible: absent skip/take returns every run. When either is
            // present the client wants a window, so page with LIMIT/OFFSET over the
            // existing started_at DESC ordering.
            if (skip.HasValue || take.HasValue)
            {
                var s = Math.Max(0, skip ?? 0);
                var t = Math.Max(0, take ?? 0);
                return Results.Json((await store.All(s, t)).Select(RunResponse.From).ToArray());
            }

            return Results.Json((await store.All()).Select(RunResponse.From).ToArray());
        });
        app.MapGet("/runs/active", async (IRunStore store) =>
            Results.Json((await store.Active()).Select(RunResponse.From).ToArray()));

        app.MapDelete("/runs/{runId:guid}", async (Guid runId, IRunStore store) =>
            await store.Delete(runId)
                ? Results.NoContent()
                : Results.NotFound());

        app.MapPost("/runs/{runId:guid}/restart", async (
            Guid runId,
            IRunStore runs,
            Api.Queue.IQueueStore queue) =>
        {
            var old = await runs.GetRun(runId);
            if (old is null)
            {
                return Results.NotFound();
            }

            if (!string.Equals(old.Status, RunStatus.Failed, StringComparison.Ordinal))
            {
                return Results.Problem(
                    "Only failed runs can be restarted.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Run is not failed");
            }

            var resumeStage = RunResume.DeriveResumeStage(old.Stages, old.CurrentCategory);
            var queueRow = await queue.PrepareResume(old.RunId, old.Branch, resumeStage, old.RunId);

            return Results.Json(new RestartRunResponse(
                old.Repo,
                old.Branch,
                old.Spec,
                resumeStage,
                old.RunId,
                queueRow?.Id));
        });

        app.MapGet("/runs/{runId:guid}/restarts", async (Guid runId, IRunStore store) =>
        {
            var existing = await store.GetRun(runId);
            if (existing is null)
            {
                return Results.NotFound();
            }

            var children = (await store.All())
                .Where(r => r.ParentRunId == runId)
                .Select(RunResponse.From)
                .ToArray();
            return Results.Json(children);
        });

        return app;
    }
}

public record RestartRunResponse(
    string Repo,
    string Branch,
    string Spec,
    string? ResumeStage,
    Guid ParentRunId,
    long? QueueId);
