namespace Api.Host;

public static class HostEndpoints
{
    public static IEndpointRouteBuilder MapHostEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/host", async (IHostStore store) =>
        {
            var state = await store.GetState();
            return state is null
                ? Results.Json(new HostResponse(false, null))
                : Results.Json(new HostResponse(state.Online, state.LastSeen));
        });

        return app;
    }
}

public record HostResponse(bool Online, DateTimeOffset? LastSeen);
