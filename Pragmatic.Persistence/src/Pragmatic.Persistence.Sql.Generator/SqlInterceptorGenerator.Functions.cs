using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The application's <c>[SqlFunction]</c>s: the implementing part of each partial method (it throws) and the registration of its renderer for the runtime path.</summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>Reads every <c>[SqlFunction]</c> of the compilation; the ones out of shape are PRAG3013 and left out.</summary>
    private static List<AppFunction> ReadFunctions(IEnumerable<IMethodSymbol> methods, Compilation compilation, List<Diagnostic> diagnostics)
    {
        var functions = new List<AppFunction>();
        foreach (var method in methods.OrderBy(m => m.ContainingType.ToDisplayString(), System.StringComparer.Ordinal).ThenBy(m => m.Name, System.StringComparer.Ordinal))
        {
            var function = AppFunction.TryRead(method, compilation, out var problem);
            if (function is null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.FunctionShape, method.Locations.FirstOrDefault() ?? Location.None, method.Name, problem));
                if (method.IsStatic && method.IsPartialDefinition && method.ContainingType.IsStatic)
                    functions.Add(AppFunction.Broken(method)); // its body still: the compiler then reports PRAG3013 alone, not a missing implementation too
            }
            else
            {
                functions.Add(function);
            }
        }

        return functions;
    }

    /// <summary>The implementing parts: the partial methods throw, since a [SqlFunction] runs only as SQL.</summary>
    private static void EmitFunctionBodies(StringBuilder declared, IEnumerable<AppFunction> functions)
    {
        foreach (var group in functions.GroupBy(f => f.Method.ContainingType, SymbolEqualityComparer.Default))
        {
            var type = (INamedTypeSymbol)group.Key!;
            var global = type.ContainingNamespace.IsGlobalNamespace;
            declared.AppendLine();
            if (!global)
            {
                declared.AppendLine($"namespace {type.ContainingNamespace.ToDisplayString()}");
                declared.AppendLine("{");
            }

            declared.AppendLine($"    static partial class {type.Name}");
            declared.AppendLine("    {");
            foreach (var function in group)
            {
                var method = function.Method;
                var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}"));
                declared.AppendLine($"        {AccessModifier(method.DeclaredAccessibility)} static partial {method.ReturnType.ToDisplayString(Annotated)} {method.Name}({parameters}) => throw new global::System.NotSupportedException(\"{method.Name} is a [SqlFunction]: it is translated to SQL inside a query's lambda and does not run in .NET.\");");
            }

            declared.AppendLine("    }");
            if (!global)
                declared.AppendLine("}");
        }
    }

    /// <summary>The registrations (in the module initializer): the method, from a delegate (no reflection by name), and its renderer.</summary>
    private static IEnumerable<FunctionRegistrationModel> FunctionRegistrations(IEnumerable<AppFunction> functions)
        => functions.Where(f => !f.IsBroken).Select(f => new FunctionRegistrationModel(
            f.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            f.Method.Name,
            f.Method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToEquatableArray(),
            f.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            f.RuntimeArms().ToEquatableArray(),
            f.IsPredicate));

    private static string AccessModifier(Microsoft.CodeAnalysis.Accessibility accessibility) => accessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Private => "private",
        _ => "internal",
    };
}
