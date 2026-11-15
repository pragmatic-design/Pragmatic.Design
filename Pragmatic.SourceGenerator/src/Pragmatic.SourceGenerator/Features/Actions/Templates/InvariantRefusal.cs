using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The statement that refuses an operation over one broken <c>[Invariant]</c>, shared by the mutation
///     and the domain-action invoker so one rule reads the same whichever operation broke it.
/// </summary>
internal static class InvariantRefusal
{
    /// <summary>
    ///     <c>return new InvariantViolationError(…) { … };</c> for the rule, read off
    ///     <paramref name="instance" />.
    /// </summary>
    /// <param name="name">How the refusal names the rule: the method, or <c>Child.Method</c> for a part's.</param>
    /// <param name="invariant">The rule.</param>
    /// <param name="instance">The expression that reaches the row that broke it, whose members fill the parameters.</param>
    /// <param name="extraInitializers">More initializers, already rendered (<c>EntityType = …</c>), or null.</param>
    /// <remarks>
    ///     A rule that declares no code, member, parameters or key renders exactly as it did before they
    ///     existed: two arguments and no initializer.
    /// </remarks>
    public static string Line(string name, InvariantModel invariant, string instance, string? extraInitializers = null)
    {
        var message = StringHelper.CSharpLiteral(invariant.Message ?? "");
        var key = invariant.MessageKey is { Length: > 0 } declared
            ? $", \"{StringHelper.CSharpLiteral(declared)}\""
            : "";

        var initializers = new System.Collections.Generic.List<string>();
        if (invariant.ErrorCode is { Length: > 0 } code)
            initializers.Add($"DeclaredCode = \"{StringHelper.CSharpLiteral(code)}\"");
        if (invariant.Member is { Length: > 0 } member)
            initializers.Add($"Member = \"{member}\"");
        if (invariant.ParameterMembers.Count > 0)
        {
            // `?? ""` because the dictionary holds no nulls, and a null fills its placeholder with nothing
            // either way: the localizer writes `value?.ToString() ?? ""`.
            var entries = invariant.ParameterMembers.Select(p => $"[\"{p}\"] = (object?){instance}.{p} ?? \"\"");
            initializers.Add("Parameters = new global::System.Collections.Generic.Dictionary<string, object> { "
                             + string.Join(", ", entries) + " }");
        }

        if (extraInitializers is not null)
            initializers.Add(extraInitializers);

        var body = initializers.Count == 0 ? "" : " { " + string.Join(", ", initializers) + " }";

        return "return new global::Pragmatic.Actions.Mutation.InvariantViolationError("
               + $"\"{name}\", \"{message}\"{key}){body};";
    }
}
