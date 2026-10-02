using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Result.SourceGenerator.Templates;

namespace Pragmatic.Result.SourceGenerator;

/// <summary>
///     Main entry point for Pragmatic.Result source generators.
///     Generates Result3-Result9 and VoidResult2-VoidResult8 variants at compile time.
/// </summary>
[Generator]
public class ResultSourceGenerator : IIncrementalGenerator
{
    private const int MaxErrorTypes = 8;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Only generate when compiling Pragmatic.Result assembly itself
        var shouldGenerate = context.CompilationProvider
            .Select((compilation, _) => compilation.AssemblyName == "Pragmatic.Result");

        context.RegisterSourceOutput(shouldGenerate, (ctx, generate) =>
        {
            if (!generate)
                return;

            // Generate Result3 through Result9 (2-8 error types as per DESIGN.md max 8)
            for (var errorCount = 2; errorCount <= MaxErrorTypes; errorCount++)
            {
                var template = new ResultVariantTemplate(errorCount);
                var artifact = template.RenderOutput();
                ctx.AddSource(artifact);
            }

            // Generate VoidResult<E1, E2> through VoidResult<E1..E8>
            for (var errorCount = 2; errorCount <= MaxErrorTypes; errorCount++)
            {
                var template = new VoidResultVariantTemplate(errorCount);
                var artifact = template.RenderOutput();
                ctx.AddSource(artifact);
            }
        });
    }
}