using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Abstractions.Analyzers;

/// <summary>
///     Flags a captive dependency (PRAG1450): a scoped service injected into the constructor of a
///     singleton-lifetime host service (a <c>BackgroundService</c> subclass or an
///     <c>IHostedService</c> implementation). The scoped instance is then captured for the whole
///     application lifetime. Currently detects the two highest-confidence cases — <c>IOptionsSnapshot&lt;T&gt;</c>
///     (always scoped) and a <c>DbContext</c> (scoped by default) — which are never correct here.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CaptiveDependencyAnalyzer : DiagnosticAnalyzer
{
    private const string BackgroundServiceMetadataName = "Microsoft.Extensions.Hosting.BackgroundService";
    private const string HostedServiceMetadataName = "Microsoft.Extensions.Hosting.IHostedService";
    private const string OptionsSnapshotMetadataName = "Microsoft.Extensions.Options.IOptionsSnapshot`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.CaptiveDependency);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var backgroundService = start.Compilation.GetTypeByMetadataName(BackgroundServiceMetadataName);
            var hostedService = start.Compilation.GetTypeByMetadataName(HostedServiceMetadataName);
            var optionsSnapshot = start.Compilation.GetTypeByMetadataName(OptionsSnapshotMetadataName);
            var dbContext = start.Compilation.GetTypeByMetadataName(DbContextMetadataName);

            // Nothing hosting-related referenced → nothing to analyze.
            if (backgroundService is null && hostedService is null)
                return;

            start.RegisterSymbolAction(
                ctx => AnalyzeType(ctx, backgroundService, hostedService, optionsSnapshot, dbContext),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context,
        INamedTypeSymbol? backgroundService,
        INamedTypeSymbol? hostedService,
        INamedTypeSymbol? optionsSnapshot,
        INamedTypeSymbol? dbContext)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract)
            return;

        if (!IsSingletonHost(type, backgroundService, hostedService))
            return;

        foreach (var ctor in type.InstanceConstructors)
        {
            foreach (var parameter in ctor.Parameters)
            {
                var (isScoped, fix) = ClassifyScoped(parameter.Type, optionsSnapshot, dbContext);
                if (!isScoped)
                    continue;

                var location = parameter.Locations.FirstOrDefault();
                if (location is null)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CaptiveDependency,
                    location,
                    type.Name,
                    parameter.Type.ToDisplayString(),
                    fix));
            }
        }
    }

    private static bool IsSingletonHost(
        INamedTypeSymbol type,
        INamedTypeSymbol? backgroundService,
        INamedTypeSymbol? hostedService)
    {
        if (backgroundService is not null)
        {
            for (var b = type.BaseType; b is not null; b = b.BaseType)
                if (SymbolEqualityComparer.Default.Equals(b, backgroundService))
                    return true;
        }

        if (hostedService is not null)
        {
            foreach (var iface in type.AllInterfaces)
                if (SymbolEqualityComparer.Default.Equals(iface, hostedService))
                    return true;
        }

        return false;
    }

    private static (bool IsScoped, string Fix) ClassifyScoped(
        ITypeSymbol parameterType,
        INamedTypeSymbol? optionsSnapshot,
        INamedTypeSymbol? dbContext)
    {
        if (optionsSnapshot is not null
            && parameterType is INamedTypeSymbol { IsGenericType: true } named
            && SymbolEqualityComparer.Default.Equals(named.ConstructedFrom, optionsSnapshot))
            return (true, "IOptionsMonitor<T>");

        if (dbContext is not null)
        {
            for (var b = parameterType as INamedTypeSymbol; b is not null; b = b.BaseType)
                if (SymbolEqualityComparer.Default.Equals(b, dbContext))
                    return (true, "IServiceScopeFactory and create a scope per unit of work");
        }

        return (false, "");
    }
}
