namespace Api.Runs;

public static class RunsEndpoints
{
    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/runs/{runId:guid}/events", async (Guid runId, RunEvent ev, IRunStore store) =>
        {
            if (string.IsNullOrWhiteSpace(ev.Type))
                return Results.BadRequest();

            await store.Apply(runId, ev);
            return Results.Accepted();
        }).AddEndpointFilter<RequireFactoryToken>();

        app.MapGet("/runs", async (IRunStore store) =>
            Results.Json((await store.All()).Select(RunResponse.From).ToArray()));
        app.MapGet("/runs/active", async (IRunStore store) =>
            Results.Json((await store.Active()).Select(RunResponse.From).ToArray()));

        return app;
    }
}
