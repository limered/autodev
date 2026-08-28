namespace Api.Queue;

public static class QueueEndpoints
{
    public static IEndpointRouteBuilder MapQueueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/queue", async (EnqueueRequest req, IQueueStore store) =>
        {
            var item = await store.Enqueue(req.IssueId);
            return item is null
                ? Results.Problem("Failed to enqueue issue.")
                : Results.Json(item);
        });

        app.MapGet("/queue", async (IQueueStore store) => Results.Json(await store.All()));

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
