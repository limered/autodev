using Npgsql;

namespace Api.Runs;

/// <summary>
/// Owns what happens when a run finishes: release its queue slot and report the
/// GitHub issue that should be closed. Participates in the caller's transaction so
/// the queue-row delete stays atomic with the run-status update; the GitHub call
/// itself is left to the caller to make after commit.
/// </summary>
public interface IRunCompletion
{
    /// <summary>
    /// Releases the finished run's queue slot within <paramref name="tx"/> and returns
    /// the linked GitHub issue to close, or <see langword="null"/> if none is linked.
    /// </summary>
    Task<(string Repo, int Number)?> OnFinished(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx);
}

public sealed class RunCompletion : IRunCompletion
{
    public async Task<(string Repo, int Number)?> OnFinished(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var issue = await GetLinkedIssue(runId, conn, tx);

        await using var deleteQueueCmd = new NpgsqlCommand(
            "DELETE FROM queue WHERE run_id = @runId;", conn, tx);
        deleteQueueCmd.Parameters.AddWithValue("runId", runId);
        await deleteQueueCmd.ExecuteNonQueryAsync();

        return issue;
    }

    private static async Task<(string Repo, int Number)?> GetLinkedIssue(
        Guid runId,
        NpgsqlConnection conn,
        NpgsqlTransaction tx)
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

        return (reader.GetString(0), reader.GetInt32(1));
    }
}
