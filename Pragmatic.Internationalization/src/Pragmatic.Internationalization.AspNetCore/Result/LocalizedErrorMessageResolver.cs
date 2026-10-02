using System.Text.RegularExpressions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Result;
using Pragmatic.Result.AspNetCore;

namespace Pragmatic.Internationalization.AspNetCore.Result;

/// <summary>
///     Resolves localized error messages by bridging <see cref="ILocalizationProvider"/>
///     to <see cref="IErrorMessageResolver"/>.
/// </summary>
/// <remarks>
///     <para>
///         Looks up <c>{messageKey}.detail</c> for the message and <c>{messageKey}.title</c> for the
///         title. Neither is a prefix of the other, which is what lets a translation file be handed
///         to the source generator: a bare <c>{messageKey}</c> beside <c>{messageKey}.title</c> asks
///         the generated key class for a member and a nested class of the same name, and is reported
///         as <c>PRAG1805</c>.
///     </para>
///     <para>
///         Supports parameter interpolation from <see cref="Error.Parameters"/>:
///         <c>"Cannot exceed {limit} nights"</c> with <c>Parameters["limit"] = 14</c>
///         produces <c>"Cannot exceed 14 nights"</c>.
///     </para>
///     <para>
///         This resolver reads <see cref="I18NContext.Current"/> at call time (not at construction),
///         so it is safe to register as singleton.
///     </para>
/// </remarks>
public sealed partial class LocalizedErrorMessageResolver(CompositeLocalizationProvider provider)
    : IErrorMessageResolver
{
    /// <summary>Suffix of the key carrying the message shown to the caller.</summary>
    public const string DetailSuffix = ".detail";

    /// <summary>Suffix of the key carrying the title.</summary>
    public const string TitleSuffix = ".title";

    private readonly CompositeLocalizationProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public string? Resolve(string code, object? context = null)
    {
        var messageKey = GetMessageKey(code, context) + DetailSuffix;
        var culture = I18NContext.Current.CultureCode;

        var message = _provider.GetString(messageKey, culture);
        if (message is null) return null;

        return InterpolateParameters(message, context);
    }

    /// <inheritdoc />
    public string? ResolveTitle(string code, object? context = null)
    {
        var messageKey = GetMessageKey(code, context);
        var titleKey = messageKey + TitleSuffix;
        var culture = I18NContext.Current.CultureCode;

        return _provider.GetString(titleKey, culture);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The key as the issue carries it, no suffix: an issue has a message and no title, so there is
    ///     nothing for a suffix to tell apart.
    /// </remarks>
    public string? ResolveKey(string messageKey, IReadOnlyDictionary<string, object>? parameters = null)
    {
        var message = _provider.GetString(messageKey, I18NContext.Current.CultureCode);
        return message is null ? null : Interpolate(message, parameters);
    }

    private static string GetMessageKey(string code, object? context)
    {
        // Use Error.MessageKey if available (it may have custom overrides)
        if (context is Error error)
            return error.MessageKey;

        // Fallback: derive from code
        return "error." + code.ToLowerInvariant().Replace('_', '.');
    }

    private static string InterpolateParameters(string message, object? context)
        => Interpolate(message, (context as Error)?.Parameters);

    private static string Interpolate(string message, IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is not { Count: > 0 })
            return message;

        return ParameterPattern().Replace(message, match =>
        {
            var paramName = match.Groups[1].Value;
            return parameters.TryGetValue(paramName, out var value)
                ? value?.ToString() ?? ""
                : match.Value;
        });
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex ParameterPattern();
}
