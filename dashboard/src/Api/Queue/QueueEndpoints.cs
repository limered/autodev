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

        return app;
    }
}

public record EnqueueRequest(long IssueId);
