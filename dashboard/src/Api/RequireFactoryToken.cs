namespace Api;

/// <summary>
/// Rejects requests missing a valid <c>X-Factory-Token</c> header (matched against
/// the <c>FACTORY_TOKEN</c> config value). Applied to the endpoints the factory host
/// calls, so the shared-secret check lives in one place instead of copy-pasted per endpoint.
/// </summary>
public sealed class RequireFactoryToken : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var factoryToken = config["FACTORY_TOKEN"];
        var provided = context.HttpContext.Request.Headers["X-Factory-Token"].ToString();

        if (string.IsNullOrEmpty(factoryToken) || provided != factoryToken)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
