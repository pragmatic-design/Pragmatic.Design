// Pragmatic.Internationalization.AspNetCore - Internationalization Step

using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Internationalization.AspNetCore.Extensions;

namespace Pragmatic.Internationalization.AspNetCore.Steps;

/// <summary>
///     Adds the I18N context middleware to the pipeline.
/// </summary>
/// <remarks>
///     <para>
///         Order 93 — after authentication (91) and tenant resolution (92), and <b>before</b>
///         authorization (94). Handlers run at the end of the pipeline, so they see the locale wherever
///         this step sits; running it ahead of routing instead would resolve the culture before anyone
///         knows who is asking, and a provider that reads the user's stored preference would always see
///         an anonymous request and never contribute.
///     </para>
///     <para>
///         ⚠️ Not after authorization: that middleware writes the only error that is not an endpoint's,
///         its 403. With no culture resolved yet, its title and detail would come from
///         <c>CultureInfo.CurrentCulture</c> — the machine's locale — and the same request would answer
///         in Italian on one machine and in English on the CI runner. 93 is the whole window this step
///         has: the user is authenticated and the tenant is resolved, so both providers can contribute,
///         and the refusal is written after the culture exists rather than before.
///     </para>
///     <para>
///         The same shape as <c>TemporalContextStep</c> at 100, which resolves the client's time zone
///         and runs late for the same reason. Middleware before 93 — CORS, antiforgery, the rate
///         limiter — sees no resolved culture; none of them localizes anything, and the rate limiter's
///         429 has no body.
///     </para>
/// </remarks>
public class InternationalizationStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 93;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UsePragmaticInternationalization();
}
