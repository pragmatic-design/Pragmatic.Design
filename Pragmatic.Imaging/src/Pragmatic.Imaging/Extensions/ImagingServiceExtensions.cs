using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Imaging.Extensions;

/// <summary>
/// DI registration for Pragmatic.Imaging services.
/// </summary>
public static class ImagingServiceExtensions
{
    /// <summary>
    /// Registers a shared <see cref="ImagingOptions"/> instance for injection.
    /// The static/fluent APIs (<see cref="ImagePipeline"/>, <see cref="ImageConverter"/>, …) do not
    /// require DI — this is a convenience for consumers that prefer to resolve options.
    /// </summary>
    /// <remarks>
    /// The hardened native DLL-import resolver is installed unconditionally at assembly load
    /// (see <see cref="Native.NativeResolver"/>), so it protects the static APIs too — calling this
    /// method is not required to get that hardening.
    /// </remarks>
    public static IServiceCollection AddPragmaticImaging(
        this IServiceCollection services,
        ImagingOptions? options = null)
    {
        services.AddSingleton(options ?? ImagingOptions.Default);
        return services;
    }
}
