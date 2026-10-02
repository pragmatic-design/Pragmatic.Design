using System.Text;
using Microsoft.Extensions.Options;

namespace Pragmatic.Identity.Local.Jwt;

/// <summary>
///     Startup validation for <see cref="JwtOptions" />. Complements the runtime guard in
///     <see cref="JwtTokenGenerator" /> by failing fast at application start when the configuration is unsafe.
///     Because it runs via <c>ValidateOnStart</c> for every <see cref="JwtOptions" /> instance the container
///     resolves, it enforces the security invariants regardless of how the options were configured (including
///     when bound directly through <c>AddOptions</c>, bypassing <see cref="PragmaticBuilderJwtExtensions" />).
/// </summary>
/// <remarks>
///     Uses explicit checks rather than <c>[Range(typeof(...))]</c> data annotations, which are
///     trim-/AOT-unfriendly.
/// </remarks>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    // A HMAC key can be 32 bytes long yet carry almost no entropy ("aaaa…", "0101…", a repeated word):
    // length measures size, not unpredictability. A key with only a handful of distinct characters is a
    // placeholder/test value, never the output of a CSPRNG, so we reject it up front. The threshold is
    // deliberately low to avoid false positives on legitimate random keys (base64/hex keys have >16
    // distinct characters); it only trips on egregious placeholders.
    private const int MinDistinctChars = 6;

    // Beyond a few minutes, ClockSkew stops absorbing clock drift and starts materially extending the
    // window in which an expired token is still accepted — a footgun rather than a tolerance.
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly bool _productionStrict;

    /// <param name="productionStrict">
    ///     When <see langword="true" />, Issuer and Audience are both required (see the class remarks): a
    ///     missing Issuer/Audience turns off <c>ValidateIssuer</c>/<c>ValidateAudience</c> and accepts tokens
    ///     minted by any issuer for any audience. Captured at registration from the host environment so the
    ///     validator stays free of environment lookups (and unit-testable).
    /// </param>
    public JwtOptionsValidator(bool productionStrict = false) => _productionStrict = productionStrict;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            errors.Add("JWT SigningKey must be configured.");
        }
        else
        {
            // HMAC-SHA256 requires a key of at least 256 bits.
            if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
                errors.Add("JWT SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256.");

            // Length is necessary but not sufficient — guard against low-entropy placeholders.
            if (DistinctCharCount(options.SigningKey) < MinDistinctChars)
                errors.Add(
                    "JWT SigningKey appears to be a low-entropy placeholder (too few distinct characters). " +
                    "Key length is not the same as key entropy: generate a random key, e.g. " +
                    "Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).");
        }

        // #ID-JWT1: a zero/negative expiration mints tokens that are already expired the moment they are
        // issued (nbf >= exp), locking every user out.
        if (options.TokenExpiration <= TimeSpan.Zero)
            errors.Add("JWT TokenExpiration must be greater than zero.");

        // #ID-JWT1: negative skew is nonsensical; an oversized skew silently keeps expired tokens valid.
        if (options.ClockSkew < TimeSpan.Zero)
            errors.Add("JWT ClockSkew must not be negative.");
        else if (options.ClockSkew > MaxClockSkew)
            errors.Add($"JWT ClockSkew must not exceed {MaxClockSkew.TotalMinutes:0} minutes.");

        // If an audience is enforced, an issuer should also be set: validating audience without a
        // trusted issuer accepts tokens minted by any issuer for that audience.
        if (!string.IsNullOrWhiteSpace(options.Audience) && string.IsNullOrWhiteSpace(options.Issuer))
            errors.Add("JWT Issuer must be configured when Audience validation is enabled.");

        // #ID-JWT2: in a production-strict environment Issuer AND Audience must both be set. Enforced HERE
        // (not only in the builder extension) so it cannot be bypassed by binding JwtOptions directly.
        if (_productionStrict
            && (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience)))
        {
            errors.Add(
                "JWT Issuer and Audience must both be configured outside Development. Leaving either empty " +
                "disables ValidateIssuer/ValidateAudience and accepts tokens for any issuer/audience.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static int DistinctCharCount(string value)
    {
        var seen = new HashSet<char>();
        foreach (var c in value)
            seen.Add(c);
        return seen.Count;
    }
}
