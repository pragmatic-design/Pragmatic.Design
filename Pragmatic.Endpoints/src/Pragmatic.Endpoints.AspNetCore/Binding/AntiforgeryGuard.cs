using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     Validates the antiforgery token before a generated endpoint reads the form.
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET refuses to hand over the form of an endpoint that requires a token unless something
///         has already validated it — <c>ReadFormAsync</c> throws, saying so. When the antiforgery
///         middleware is in the pipeline it does that and leaves an
///         <see cref="IAntiforgeryValidationFeature" /> behind; when it is not, the minimal-API binder
///         would validate on the spot. A generated endpoint binds its own parameters, so it answers
///         400 instead of letting `ReadFormAsync` throw a 500 the endpoint never declared.
///     </para>
///     <para>
///         Only for endpoints that ask for it: <c>DisableAntiforgery()</c> sets metadata saying the
///         opposite, and form endpoints get that by default.
///     </para>
/// </remarks>
public static class AntiforgeryGuard
{
    /// <summary>
    ///     Whether the request may proceed to read its form.
    /// </summary>
    /// <returns><c>false</c> when a required token is missing or invalid; the caller answers 400.</returns>
    public static bool Validate(HttpContext context)
    {
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { } validated)
            return validated.IsValid;

        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>();
        if (metadata is not { RequiresValidation: true })
            return true;

        // The endpoint demands a token and nothing has validated one. Refuse, rather than validate
        // here: `IAntiforgery.ValidateRequestAsync` reads the form to find the token, and reading the
        // form is exactly what ASP.NET refuses until validation has happened — the two would deadlock
        // on each other. Validation belongs to `UseAntiforgery()`, which leaves the feature behind.
        context.Features.Set<IAntiforgeryValidationFeature>(Result.Invalid);
        return false;
    }

    private sealed class Result(bool isValid) : IAntiforgeryValidationFeature
    {
        public static readonly Result Valid = new(true);
        public static readonly Result Invalid = new(false);

        public bool IsValid { get; } = isValid;

        public Exception? Error => null;
    }
}
