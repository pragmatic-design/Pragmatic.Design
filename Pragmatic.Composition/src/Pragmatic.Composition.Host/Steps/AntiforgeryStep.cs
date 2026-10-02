using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds the antiforgery middleware for endpoints marked [RequireAntiforgery].
///     Order 80 — after authentication/authorization (the token is user-bound) and CORS (75).
///     The middleware only enforces on endpoints carrying antiforgery metadata, so it is a
///     pass-through for everything else. The antiforgery SERVICES are registered by the
///     generated host alongside this step (DI-registered steps only get ConfigurePipeline).
/// </summary>
public class AntiforgeryStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 80;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UseAntiforgery();
}
