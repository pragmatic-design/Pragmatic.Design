using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Default implementation of <see cref="IPragmaticBuilder" />.
///     Created by the generated <c>PragmaticApp.RunAsync</c> and passed to the configure callback.
/// </summary>
public sealed class PragmaticBuilder(
    IServiceCollection services,
    IConfiguration configuration,
    IHostEnvironment environment) : IPragmaticBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; } = services;

    /// <inheritdoc />
    public IConfiguration Configuration { get; } = configuration;

    /// <inheritdoc />
    public IHostEnvironment Environment { get; } = environment;

    /// <summary>
    ///     Gets the cross-cutting hosting options (telemetry, maintenance mode).
    ///     Used internally by the generated host code.
    /// </summary>
    public PragmaticOptions Options { get; } = new();
}
