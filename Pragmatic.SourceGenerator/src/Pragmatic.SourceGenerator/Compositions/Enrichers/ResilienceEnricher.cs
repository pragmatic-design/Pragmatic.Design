using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Compositions.Models;

namespace Pragmatic.SourceGenerator.Compositions.Enrichers;

/// <summary>
///     Detects [ResiliencePolicy("name")] attribute on a symbol
///     and produces a <see cref="ResilienceContribution"/>.
/// </summary>
internal static class ResilienceEnricher
{
    private const string AttributeFqn = "Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute";

    /// <summary>
    ///     Returns the contribution, plus a flag saying the attribute named an empty policy.
    /// </summary>
    /// <remarks>
    ///     An empty or whitespace-only name contributes nothing and is reported as PRAG0420 by the
    ///     Actions feature. Propagated verbatim into the generated invoker, it would ask the registry
    ///     for a policy that no configuration can register — a lookup failure at runtime, on the first
    ///     call, with nothing said at compile time.
    /// </remarks>
    public static (ResilienceContribution? Contribution, bool HasBlankPolicyName) Enrich(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            if (attrClass.ToDisplayString() != AttributeFqn)
                continue;

            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string policyName)
            {
                if (string.IsNullOrWhiteSpace(policyName))
                    return (null, true);

                return (new ResilienceContribution { PolicyName = policyName }, false);
            }
        }

        return (null, false);
    }
}
