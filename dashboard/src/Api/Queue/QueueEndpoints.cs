namespace Api.Queue;

public static class QueueEndpoints
{
    public static IEndpointRouteBuilder MapQueueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/queue", async (EnqueueRequest req, IQueueStore store) =>
        {
            var row = await store.Enqueue(req.IssueId);
            return row is null
                ? Results.Problem("Failed to enqueue issue.")
                : Results.Json(row);
        });

        app.MapGet("/queue", async (IQueueStore store) =>
            Results.Json(await store.All()));

        app.MapPost("/queue/{id:long}/start-next", async (long id, IQueueStore store) =>
        {
            var row = await store.StartNext(id);
            return row is null ? Results.NotFound() : Results.Json(row);
        });

        app.MapPost("/queue/{id:long}/restart", async (long id, IQueueStore store) =>
        {
            var row = await store.Restart(id);
            return row is null ? Results.NotFound() : Results.Json(row);
        });

        app.MapPost("/queue/claim-next", async (IQueueStore store) =>
        {
            var claim = await store.ClaimNext();
            return claim is null ? Results.NoContent() : Results.Json(claim);
        }).AddEndpointFilter<RequireFactoryToken>();

        app.MapPatch("/queue/order", async (ReorderRequest req, IQueueStore store) =>
        {
            await store.Reorder(req.Ids);
            return Results.NoContent();
        });

        app.MapDelete("/queue/{id:long}", async (long id, IQueueStore store) =>
            await store.Delete(id)
                ? Results.NoContent()
                : Results.NotFound());

        return app;
    }
}

public record EnqueueRequest(long IssueId);

public record ReorderRequest(long[] Ids);
