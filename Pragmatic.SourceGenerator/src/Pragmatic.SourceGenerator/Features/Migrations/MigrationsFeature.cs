using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Migrations.Templates;

namespace Pragmatic.SourceGenerator.Features.Migrations;

/// <summary>
///     Tells the migrations runtime which database drivers this application can actually use.
/// </summary>
/// <remarks>
///     The whole feature is one module initializer. It exists because the question "is Npgsql
///     available?" asked at run time, by an assembly that references no driver, can only be asked by
///     name — <c>Type.GetType</c> plus <c>Activator.CreateInstance</c>. <c>FeatureDetector</c> answers
///     it on the compilation that does reference the driver, which turns reflective calls into
///     constructor calls.
/// </remarks>
internal static class MigrationsFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var generatedNamespace = context.CompilationProvider
            .Select(static (compilation, _) => $"{compilation.AssemblyName ?? "Global"}.Generated");

        context.RegisterSourceOutputSafe(
            features.Combine(generatedNamespace),
            static (ctx, pair) =>
            {
                var (detected, generatedNs) = pair;
                if (!detected.HasMigrations)
                    return;

                var template = new MigrationDriverRegistrationTemplate(
                    generatedNs,
                    detected.HasNpgsqlDriver,
                    detected.HasSqlServerDriver,
                    detected.HasSqliteDriver);

                // Nothing to say when the host references the migrations runtime but no driver: the
                // registry stays empty and MigrationsBuilder registers no provider, which is the same
                // outcome the run-time probe produced.
                if (!detected.HasNpgsqlDriver && !detected.HasSqlServerDriver && !detected.HasSqliteDriver)
                    return;

                ctx.AddSource(template.RenderOutput());
            });
    }
}
