using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A declared chain called in place (<c>Chains.OpenOrders(session.Query&lt;Order&gt;(), status)</c>): its body's steps are replayed
///     here as if written at the call site, with the definition's parameters bound from slots the call's interceptor fills (one
///     per parameter, in order). The same chain is thus a root, a join's inner chain or a set operation's member, and its SQL is
///     part of the one statement. A declared chain does not call another, and takes its values from its parameters only.
/// </summary>
internal sealed partial class PlanBuilder
{
    private Dictionary<IParameterSymbol, string>? _fragmentArgs; // while replaying a declared chain: its parameters as plan variables

    private bool AddFragment(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        if (_fragmentArgs is not null)
            return Refuse("a declared chain does not call another declared chain: inline its steps");
        if (step.Inner is null || step.Method.Parameters.Length == 0)
            return Refuse("the declared chain's body must be one chain on its first parameter, written with the operators the generator translates");
        var arguments = step.Invocation.ArgumentList.Arguments;
        if (_valuesInSlots && arguments.Count != step.Method.Parameters.Length)
            return Refuse("a declared chain is called with every parameter written out (no defaults, no named skips)");
        foreach (var inner in step.Inner)
        {
            if (inner.IsFragment)
                return Refuse("a declared chain does not call another declared chain: inline its steps");
            if (inner.UsesSlot)
                return Refuse($"inside a declared chain the values are the method's parameters ('{inner.Name}' with an argument of its own is not): use the one-parameter form");
        }

        // At a call site: one slot per parameter, in order, each read into a typed variable the body's lambdas are translated against.
        // In a [Query] definition: the parameter stands for the argument as written there, and the values go in the definition's slots as usual.
        var args = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        for (var j = 1; j < step.Method.Parameters.Length; j++)
        {
            var parameter = step.Method.Parameters[j];
            if (_valuesInSlots)
            {
                args[parameter] = $"({arguments[j]})";
                continue;
            }

            var variable = $"frag{_binds.Count}_{parameter.Name}"; // the binds are shared by the plan's members and companions: the count keeps the names apart
            _binds.Add($"var {variable} = ({parameter.Type.ToDisplayString(SqlInterceptorGenerator.Annotated)})q.Slot({_slot++})!;");
            args[parameter] = variable;
        }

        _fragmentArgs = args;
        try
        {
            foreach (var inner in step.Inner)
            {
                if (AddStep(inner, inner.Model ?? model))
                    continue;
                StepError ??= LastError is { } why ? $"in the declared chain {step.Method.Name}, '{LastErrorExpression}': {why}" : $"the declared chain {step.Method.Name} has a step outside the translated subset ('{inner.Name}')";
                return false;
            }
        }
        finally
        {
            _fragmentArgs = null;
        }

        return true;
    }

    /// <summary>The declared chain's parameters bound on a translator created while its body is replayed.</summary>
    private LambdaTranslator Bound(LambdaTranslator translator)
    {
        translator.MarkJoin = MarkJoin; // a navigation's join carries its target's rules, resolved when the chain is complete
        if (_fragmentArgs is not null)
        {
            foreach (var pair in _fragmentArgs)
                translator.BindParameter(pair.Key, pair.Value);
        }

        return translator;
    }
}
