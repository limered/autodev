using Npgsql;

namespace Api.Issues;

/// <summary>
/// The SQL <see cref="IIssueResolver"/>: answers the seam's cross-domain questions by
/// running the issues/queue SQL on the caller's open connection and transaction, so
/// the caller's lock and atomicity cover the answer. It owns no connection of its own.
/// </summary>
public sealed class IssueResolver : IIssueResolver
{
    public async Task<IssueClaimPayload?> ResolveClaimPayloadAsync(long issueId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT i.repo, i.body, i.title FROM issues i WHERE i.github_id = @issueId;", conn, tx);
        cmd.Parameters.AddWithValue("issueId", issueId);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return IssueClaimRules.PayloadFor(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    public async Task<LinkedIssue?> ResolveLinkedIssueAsync(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT i.repo, i.number
            FROM queue q
            JOIN issues i ON i.github_id = q.issue_id
            WHERE q.run_id = @runId;
            """, conn, tx);
        cmd.Parameters.AddWithValue("runId", runId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new LinkedIssue(reader.GetString(0), reader.GetInt32(1));
    }

    public async Task ReleaseQueueSlotAsync(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM queue WHERE run_id = @runId;", conn, tx);
        cmd.Parameters.AddWithValue("runId", runId);
        await cmd.ExecuteNonQueryAsync();
    }
}
