namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
///     A visibility rule nested in a trait's child an engine persists, declared on it with <c>[VisibleWhen]</c>
///     (<see cref="Templates.TraitEngineRuleTemplate" />).
/// </summary>
/// <param name="Namespace">The child's namespace.</param>
/// <param name="ChildTypeName">The child (<c>ReviewComment</c>).</param>
/// <param name="RuleName">The nested type (<c>ModerationRule</c>).</param>
/// <param name="Predicate">The rule's lambda body over <c>e</c>, fully qualified.</param>
/// <param name="Bypass">
///     The permission that lifts it, which makes the rule a permission-based filter (the engines' permission family); null for a rule lifted by
///     its name only.
/// </param>
/// <param name="Summary">The rule's documentation.</param>
internal sealed record TraitEngineRuleModel(string Namespace, string ChildTypeName, string RuleName, string Predicate, string? Bypass, string Summary);
