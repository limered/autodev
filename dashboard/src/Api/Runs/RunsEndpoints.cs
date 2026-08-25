namespace Api.Runs;

public static class RunsEndpoints
{
    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/runs/{runId:guid}/events", async (Guid runId, RunEvent ev, IRunStore store, IConfiguration config, HttpRequest req) =>
        {
            var factoryToken = config["FACTORY_TOKEN"];
            if (string.IsNullOrEmpty(factoryToken) || req.Headers["X-Factory-Token"].ToString() != factoryToken)
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(ev.Type))
                return Results.BadRequest();

            await store.Apply(runId, ev);
            return Results.Accepted();
        });

        app.MapGet("/runs", async (IRunStore store) => Results.Json(await store.All()));
        app.MapGet("/runs/active", async (IRunStore store) => Results.Json(await store.Active()));

        return app;
    }
}
