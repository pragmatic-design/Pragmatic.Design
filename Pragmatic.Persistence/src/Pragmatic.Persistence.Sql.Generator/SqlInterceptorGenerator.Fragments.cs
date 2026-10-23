using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Declared chains: a <c>[Query]</c> method returning <c>SqlQuery&lt;T&gt;</c>, whose body is a chain on its first parameter. The
///     definition is validated once; each call (<c>Chains.OpenOrders(session.Query&lt;Order&gt;(), status)</c>) is intercepted to store the
///     arguments in the chain's next slots, and the plan at the terminal replays the body's steps (<see cref="PlanBuilder" />).
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>The call of a declared chain: validated by replaying the chain up to it; the interceptor stores one slot per argument.</summary>
    private static bool EmitFragmentCall(StringBuilder source, CallSite call, EntityModel entity, System.Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics)
    {
        var where = call.Invocation.GetLocation();
        var method = call.Method;
        var body = CallSite.FragmentSteps(method, call.Model.Compilation, default);
        if (body is null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, method.Name, "the declared chain's body must be one chain on its first parameter (see the diagnostics on the definition)"));
            return false;
        }

        var steps = new List<Step>(call.Before) { new("Fragment", call.Invocation, method) { Inner = body, Model = call.Model } };
        var probe = new PlanBuilder(entity, entities, call.Model.Compilation, valuesInSlots: false);
        probe.Prepare(steps, "");
        foreach (var step in steps)
        {
            if (probe.AddStep(step, call.Model))
                continue;
            if (ReferenceEquals(step, steps[steps.Count - 1]))
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, method.Name, probe.StepError ?? "the declared chain is outside the translated subset here"));
            return false;
        }

        var offset = call.Before.Sum(s => s.SlotCount);
        var count = method.Parameters.Length - 1;
        if (offset + count > MaxSlots)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, method.Name, $"a chain holds at most {MaxSlots} operators with an argument, the declared chain's parameters included"));
            return false;
        }

        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}"));
        var chain = new StringBuilder(method.Parameters[0].Name);
        for (var j = 1; j < method.Parameters.Length; j++)
            chain.Append($".WithSlot({offset + j - 1}, {method.Parameters[j].Name})");
        source.AppendLine($"        {attribute}");
        source.AppendLine($"        internal static {method.ReturnType.ToDisplayString(Annotated)} {method.Name}_{index}({parameters}) => {chain};");
        source.AppendLine();
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, method.Name, string.Join(" ", body.Select(s => s.Name))));
        return true;
    }

    /// <summary>A declared chain's definition: its body validated as a chain (one slot per parameter); nothing is generated for it, its calls are.</summary>
    private static void EmitFragmentDefinition(IMethodSymbol method, MethodDeclarationSyntax syntax, ExpressionSyntax body, INamedTypeSymbol entityType, EntityModel entity, System.Func<INamedTypeSymbol, EntityModel?> entities, Compilation compilation, List<Diagnostic> diagnostics)
    {
        var where = method.Locations.FirstOrDefault() ?? Location.None;
        void Shape(string why) => diagnostics.Add(Diagnostic.Create(Diagnostics.QueryShape, where, method.Name, why));
        if (body is not InvocationExpressionSyntax)
        {
            Shape("a declared chain's body is one expression: the chain on the first parameter, without a terminal");
            return;
        }

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var steps = new List<Step>();
        var rooted = CallSite.Walk(body, model, default, steps, 0, parameterRoot: true);
        if (rooted is null || !SymbolEqualityComparer.Default.Equals(rooted, entityType))
        {
            Shape("the chain must start at the first parameter and use only SqlQuery operators");
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals((method.ReturnType as INamedTypeSymbol)?.TypeArguments.FirstOrDefault(), entityType) && steps.All(s => s.Name is not ("Select" or "Project" or "Join" or "LeftJoin")))
        {
            Shape("the chain returns the entity it starts from (a Select, a Project or a join changes the row type)");
            return;
        }

        // The body replayed as a call would replay it, with its parameters as values: every step must translate.
        var probe = new PlanBuilder(entity, entities, compilation, valuesInSlots: false);
        var call = new Step("Fragment", (InvocationExpressionSyntax)body, method) { Inner = steps, Model = model };
        probe.Prepare([call], "ToListAsync");
        if (!probe.AddStep(call, model))
            Shape(probe.StepError ?? "a step of the chain is outside the translated subset");
        else
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, "[Query] chain " + method.Name, string.Join(" ", steps.Select(s => s.Name))));
    }
}
