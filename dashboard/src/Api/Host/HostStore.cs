using Npgsql;

namespace Api.Host;

public interface IHostStore
{
    Task StampLastSeen();
    Task<HostState?> GetState();
}

public record HostState(DateTimeOffset LastSeen, bool Online);

public sealed class HostStore : IHostStore
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(20);

    public HostStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task StampLastSeen()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO host (id, last_seen)
            VALUES (1, now())
            ON CONFLICT (id) DO UPDATE SET last_seen = now();
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<HostState?> GetState()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT last_seen FROM host WHERE id = 1;",
            conn);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
        {
            return null;
        }

        var lastSeen = new DateTimeOffset(DateTime.SpecifyKind((DateTime)result, DateTimeKind.Utc));
        var online = DateTimeOffset.UtcNow - lastSeen <= OnlineThreshold;
        return new HostState(lastSeen, online);
    }
}
