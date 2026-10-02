using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.Documents.Csv.Generator.Diagnostics;
using Pragmatic.Documents.Csv.Generator.Models;
using Pragmatic.Documents.Csv.Generator.Templates;
using Pragmatic.Documents.Csv.Generator.Transforms;

namespace Pragmatic.Documents.Csv.Generator;

/// <summary>
/// Incremental source generator for [CsvSerializable] types.
/// Generates a nested Csv class with typed Write/Read methods — zero reflection, AOT-safe.
/// </summary>
[Generator]
public sealed class CsvSourceGenerator : IIncrementalGenerator
{
    private const string CsvSerializableAttributeFqn = "Pragmatic.Documents.Csv.CsvSerializableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CsvSerializableAttributeFqn,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
                    return CsvTransform.Extract(symbol);
                })
            .Where(static model => model is not null);

        context.RegisterSourceOutput(provider, static (ctx, model) =>
        {
            // Warn on properties whose type cannot be read back (write-only). The generated reader
            // omits them, so the code still compiles — the diagnostic tells the author why the value
            // won't round-trip.
            foreach (var p in model!.Properties)
            {
                if (p.Kind == CsvPropertyKind.Other)
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        CsvDiagnostics.UnsupportedPropertyType, Location.None,
                        model.TypeName, p.PropertyName, p.TypeName));
            }

            var template = new CsvSerializerTemplate(model);
            // Through the extension, not AddSource(hintName, source): a template that declines renders
            // to empty content, and emitting it would put an empty .g.cs into the compilation.
            ctx.AddSource(template.RenderOutput());
        });
    }
}
