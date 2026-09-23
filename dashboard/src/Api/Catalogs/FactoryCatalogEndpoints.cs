namespace Api.Catalogs;

public static class FactoryCatalogEndpoints
{
    public static IEndpointRouteBuilder MapFactoryCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/workflows", (IHostEnvironment env) =>
        {
            try
            {
                return Results.Json(FactoryCatalog.Read(FactoryCatalog.Locate(env.ContentRootPath)));
            }
            catch (FactoryCatalogException ex)
            {
                return Results.Problem(
                    ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Factory catalog invalid");
            }
        });

        return app;
    }
}
