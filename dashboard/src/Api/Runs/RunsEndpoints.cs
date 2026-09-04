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

        return app;
    }
}
