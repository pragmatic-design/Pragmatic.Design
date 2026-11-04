using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     The stores of the packages imported into a boundary (<see cref="PackageStores" />): the engine of the boundary the import named,
///     and the registration of that engine's store, which the boundary's generated registration calls.
/// </summary>
internal static partial class ActionsFeature
{
    /// <summary>
    ///     <paramref name="boundary" /> with the engine of the boundary the import named and the registrations of the stores its
    ///     operations ask for; <c>PRAG0469</c> for a store whose engine's package the compilation does not reference.
    /// </summary>
    private static BoundaryModel WithPackageStores(
        SourceProductionContext context,
        Compilation compilation,
        BoundaryModel boundary,
        PackageImport? import,
        ImmutableArray<PackageActionRegistration> registrations)
    {
        if (import is null || string.IsNullOrEmpty(boundary.PackageBoundaryKey))
            return boundary;

        var key = boundary.PackageBoundaryKey!;
        var symbol = compilation.GetTypeByMetadataName(key.StartsWith("global::", StringComparison.Ordinal) ? key.Substring(8) : key);
        var engine = symbol is null ? null : PersistenceEngineMarker.EngineOf(PersistenceEngineMarker.NamedOn(symbol), FeatureDetector.Detect(compilation));

        var calls = ImmutableArray.CreateBuilder<string>();
        foreach (var contract in registrations.SelectMany(r => r.BoundaryKeyedServices.AsImmutableArray()).Distinct(StringComparer.Ordinal))
        {
            if (PackageStores.OfContract(contract) is not { } store || PackageStores.On(store, engine) is not { } on)
                continue;

            if (!PackageStores.IsReferenced(compilation, on.Registration))
            {
                context.ReportDiagnostic(Diagnostic.Create(ActionsDiagnostics.PackageStoreNotReferenced, import.Location?.ToLocation(),
                    store.PackageAssembly, key, engine, on.Package));
                continue;
            }

            calls.Add(on.Registration);
        }

        return boundary with { PackageBoundaryEngine = engine, PackageStoreRegistrations = calls.ToImmutable() };
    }
}
