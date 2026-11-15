namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One <c>[RequiresFeature]</c> of an operation: the flag the invoker asks <c>IFeatureFlags</c> about before
///     the operation runs, by name or by its <c>IFeatureFlag</c> type.
/// </summary>
internal sealed record FeatureGateModel
{
    /// <summary>The flag's name, from <c>[RequiresFeature("name")]</c>; null for the generic form.</summary>
    public string? FlagName { get; init; }

    /// <summary>The flag type, fully qualified, from <c>[RequiresFeature&lt;TFlag&gt;]</c>; null for the named form.</summary>
    public string? FlagTypeFullName { get; init; }

    /// <summary>The expression that evaluates the flag, given the <c>IFeatureFlags</c> local and the token.</summary>
    public string Evaluation(string flags, string cancellation)
        => FlagTypeFullName is not null
            ? $"{flags}.IsEnabledAsync<{FlagTypeFullName}>({cancellation})"
            : $"{flags}.IsEnabledAsync(\"{Pragmatic.SourceGen.StringHelper.CSharpLiteral(FlagName ?? "")}\", {cancellation})";
}
