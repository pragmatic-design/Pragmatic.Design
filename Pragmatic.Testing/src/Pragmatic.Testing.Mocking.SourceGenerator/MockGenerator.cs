using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Mocking.SourceGenerator.Diagnostics;
using Pragmatic.Testing.Mocking.SourceGenerator.Models;
using Pragmatic.Testing.Mocking.SourceGenerator.Templates;
using Pragmatic.Testing.Mocking.SourceGenerator.Transforms;

namespace Pragmatic.Testing.Mocking.SourceGenerator;

/// <summary>
///     Generates a mock class for every <c>[assembly: GenerateMock&lt;T&gt;]</c> in a test project,
///     replacing the dynamic-proxy substitute library: the mock is ordinary compiled code, so it
///     carries no reflection and works under Native AOT.
/// </summary>
[Generator]
public sealed class MockGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "Pragmatic.Testing.Mocking.GenerateMockAttribute`1";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (_, _) => true,
                transform: static (ctx, _) => Extract(ctx))
            .Where(static d => d is not null)
            .Select(static (d, _) => d!)
            .Collect();

        context.RegisterSourceOutput(declarations, static (ctx, all) => Emit(ctx, all));
    }

    /// <summary>
    ///     One assembly-level attribute can carry several declarations, so every matching attribute on
    ///     the target is read rather than just the one that triggered the match.
    /// </summary>
    private static DeclaredMocks? Extract(GeneratorAttributeSyntaxContext ctx)
    {
        var declared = new List<DeclaredMock>();

        foreach (var attribute in ctx.Attributes)
        {
            if (attribute.AttributeClass?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol target)
                continue;

            var nameOverride = attribute.NamedArguments
                .FirstOrDefault(a => a.Key == "Name").Value.Value as string;

            declared.Add(new DeclaredMock(target, nameOverride, attribute.ApplicationSyntaxReference));
        }

        return declared.Count == 0 ? null : new DeclaredMocks(declared);
    }

    private static void Emit(SourceProductionContext ctx, IEnumerable<DeclaredMocks> all)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var declaration in all.SelectMany(static d => d.Items))
        {
            var target = declaration.Target;
            var location = declaration.SyntaxReference is { } reference
                ? Location.Create(reference.SyntaxTree, reference.Span)
                : Location.None;

            var key = target.ToDisplayString();
            if (!seen.Add(key))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(MockDiagnostics.DuplicateDeclaration, location, key));
                continue;
            }

            var model = MockTransform.Build(target, declaration.NameOverride);
            if (model is null)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(MockDiagnostics.NotAnInterface, location, key));
                continue;
            }

            ReportUnconfigurableMembers(ctx, location, model);

            var artifact = new MockTemplate(model).RenderOutput();
            ctx.AddSource(artifact);
        }
    }

    /// <summary>
    ///     Says which members the generated mock will implement but not let the test drive. Silence
    ///     here would mean discovering it from a member that quietly returns default.
    /// </summary>
    private static void ReportUnconfigurableMembers(SourceProductionContext ctx, Location location, MockModel model)
    {
        var byName = model.Methods.GroupBy(static m => m.Name);

        foreach (var group in byName)
        {
            var method = group.First();
            if (method.Configurable)
                continue;

            // Only generic methods remain unconfigurable; overloads each get their own member now.
            if (method.TypeParameters.Count == 0)
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                MockDiagnostics.GenericMember, location, model.InterfaceShortName, method.Name));
        }
    }

    /// <summary>The declarations found on one attribute target.</summary>
    private sealed record DeclaredMocks(IReadOnlyList<DeclaredMock> Items);

    /// <summary>A single <c>[GenerateMock&lt;T&gt;]</c>.</summary>
    private sealed record DeclaredMock(
        INamedTypeSymbol Target,
        string? NameOverride,
        SyntaxReference? SyntaxReference);
}
