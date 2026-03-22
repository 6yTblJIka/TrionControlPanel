using Trion.Core.Setup;

namespace Trion.Web.Middleware;

/// <summary>
/// Redirects every request to /setup when the first-run wizard has not yet been completed.
/// Static assets, the setup page itself, and Blazor infrastructure are always allowed through.
/// </summary>
public sealed class SetupGateMiddleware(RequestDelegate next)
{
    // Prefixes that are always allowed through, even before setup is done
    private static readonly string[] _bypassPrefixes =
    [
        "/setup",
        "/culture/",
        "/_blazor",
        "/_framework",
        "/_content/",
        "/css/",
        "/js/",
        "/lib/",
        "/favicon",
    ];

    public async Task InvokeAsync(HttpContext ctx, SetupService setupService)
    {
        var path = ctx.Request.Path.Value ?? "/";

        if (!ShouldGate(path) || await setupService.IsCompletedAsync(ctx.RequestAborted))
        {
            await next(ctx);
            return;
        }

        ctx.Response.Redirect("/setup");
    }

    private static bool ShouldGate(string path)
    {
        var lower = path.ToLowerInvariant();

        foreach (var prefix in _bypassPrefixes)
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return false;

        // Static file extensions are never gated
        if (Path.HasExtension(lower))
            return false;

        return true;
    }
}
