namespace Payments.Api.Auth;

public static class ApiKeyFilters
{
    public static RouteGroupBuilder RequireApiKey(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            string? raw = http.Request.Headers["X-Api-Key"];

            if (string.IsNullOrWhiteSpace(raw))
            {
                http.Response.Headers.WWWAuthenticate = "ApiKey";
                return ProblemMapper.Auth(StatusCodes.Status401Unauthorized, "api_key_required",
                    "Send your API key in the X-Api-Key header.", http);
            }

            var store = http.RequestServices.GetRequiredService<IApiKeyStore>();
            var caller = await store.FindAsync(ApiKeyHash.Of(raw), http.RequestAborted);

            if (caller is null)
            {
                http.Response.Headers.WWWAuthenticate = "ApiKey";
                return ProblemMapper.Auth(StatusCodes.Status401Unauthorized, "api_key_invalid",
                    "The API key is unknown or revoked.", http);
            }

            http.Items[typeof(ApiCaller)] = caller;
            return await next(ctx);
        });

        return group;
    }

    public static RouteHandlerBuilder RequireScope(this RouteHandlerBuilder b, string scope) =>
        b.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;

            if (!http.Caller().Scopes.Contains(scope))
                return ProblemMapper.Auth(StatusCodes.Status403Forbidden, "insufficient_scope",
                    $"This call needs the scope {scope}.", http,
                    extra: ("required_scope", scope));

            return await next(ctx);
        });

    public static ApiCaller Caller(this HttpContext http) =>
        (ApiCaller)http.Items[typeof(ApiCaller)]!;
}