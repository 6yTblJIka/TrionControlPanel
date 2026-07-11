using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Trion.Core.Plugins;

/// <summary>
/// Entry point for a Trion plugin. Compile your plugin as a .NET 9 class library,
/// drop the DLL into the <c>plugins/</c> folder next to the Trion.Web binary and
/// restart -- Trion discovers all types implementing this interface automatically.
/// </summary>
public interface ITrionPlugin
{
    /// <summary>Unique human-readable name shown in the Plugins page (e.g. "WoW Stats Dashboard").</summary>
    string Name { get; }

    /// <summary>Semantic version string (e.g. "1.0.0").</summary>
    string Version { get; }

    /// <summary>Short description shown on the Plugins management page.</summary>
    string Description { get; }

    /// <summary>Author name or organisation.</summary>
    string Author { get; }

    /// <summary>
    /// Called during application startup before the DI container is built.
    /// Register scoped/singleton/transient services here and add sidebar nav items
    /// via <paramref name="navRegistry"/>.
    /// </summary>
    void ConfigureServices(
        IServiceCollection services,
        IConfiguration     configuration,
        IPluginNavRegistry navRegistry);

    /// <summary>
    /// Called after <see cref="WebApplication"/> is built to map Minimal API endpoints.
    /// Override only when your plugin exposes HTTP routes; default is a no-op.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder app) { }
}
