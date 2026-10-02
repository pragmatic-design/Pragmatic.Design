namespace Pragmatic.SourceGenerator.Core;

/// <summary>
/// Helpers for generating Roslyn hint names.
///
/// Sorting convention (underscore sorts AFTER letters in case-insensitive order):
///
///   MODULE (type-first — find by entity name):
///     {TypeName}.{Artifact}.g.cs                    Invoice.Repository.g.cs
///     _Boundary.{Name}.{Artifact}.g.cs              _Boundary.Billing.Interface.g.cs
///     _Infra.{Category}.{Extension}.g.cs            _Infra.Actions.Registration.g.cs
///     _Metadata.{Category}.g.cs                     _Metadata.Persistence.g.cs
///
///   HOST (function-first — find by purpose):
///     DbContext.{Boundary}.g.cs                     DbContext.Billing.g.cs
///     EntityConfig.{Entity}.g.cs                    EntityConfig.Invoice.g.cs
///     Host.{Artifact}.g.cs                          Host.Services.g.cs
///     _Infra.{Category}.{Extension}.g.cs            _Infra.Persistence.DbContextRegistration.g.cs
///     _Metadata.{Category}.g.cs                     _Metadata.Actions.g.cs
///
/// Slashes in Roslyn hint names are unreliable across IDE versions (see dotnet/roslyn#70859).
/// </summary>
internal static class VirtualFolderHints
{
    /// <summary>
    /// Per-type hint (modules): <c>Invoice.Repository.g.cs</c>, or namespace-qualified
    /// <c>Sales.Invoice.Repository.g.cs</c> when <paramref name="namespacePrefix"/> is supplied.
    /// </summary>
    /// <remarks>
    /// The namespace prefix is required to keep hint names unique: two types with the same simple name in
    /// different namespaces (e.g. <c>Sales.Invoice</c> and <c>Archive.Invoice</c>) would otherwise produce
    /// the same hint, and <c>AddSource</c> throws on a duplicate hint — killing ALL code generation.
    /// </remarks>
    public static string ForType(string triggerTypeName, string artifact, string? namespacePrefix = null)
        => string.IsNullOrEmpty(namespacePrefix) || namespacePrefix == "<global namespace>"
            ? $"{triggerTypeName}.{artifact}.g.cs"
            : $"{namespacePrefix}.{triggerTypeName}.{artifact}.g.cs";

    /// <summary>
    /// Assembly-level infra hint (modules + host): <c>_Infra.Actions.Registration.g.cs</c>
    /// </summary>
    /// <remarks>
    ///     There is one such file per category per assembly, so the hint needs no namespace
    ///     qualifier to stay unique — unlike <see cref="ForType"/> and <see cref="ForEntityConfig"/>,
    ///     which do. It takes no <c>namespacePrefix</c>: a parameter that is silently ignored would
    ///     have callers deriving a prefix whose only consumer is that discarded parameter.
    /// </remarks>
    public static string ForAssembly(string category, string extension)
        => $"_Infra.{category}.{extension}.g.cs";

    /// <summary>
    /// Metadata hint: <c>_Metadata.Persistence.g.cs</c>
    /// </summary>
    /// <remarks>One per category per assembly — see <see cref="ForAssembly"/> on why no prefix is needed.</remarks>
    public static string ForMetadata(string category)
        => $"_Metadata.{category}.g.cs";

    /// <summary>
    /// Boundary infra hint (modules): <c>_Boundary.Billing.Interface.g.cs</c>
    /// </summary>
    public static string ForBoundary(string boundaryName, string artifact)
        => $"_Boundary.{boundaryName}.{artifact}.g.cs";

    /// <summary>
    /// Host-level EntityConfig hint: <c>EntityConfig.Sales.Invoice.g.cs</c>
    /// </summary>
    /// <remarks>
    ///     Pass <paramref name="namespacePrefix" /> whenever it is available. Two entities sharing a
    ///     simple name across namespaces (<c>Sales.Invoice</c> and <c>Archive.Invoice</c>, or the same
    ///     trait applied to same-named parents in two boundaries) otherwise produce the same hint.
    ///     Roslyn catches a duplicate hint while merging the outputs of different registrations —
    ///     outside any generator try/catch — and reacts by discarding this generator's entire output
    ///     with a CS8785 <em>warning</em>: silent total loss unless the project treats warnings as
    ///     errors. The parameter is optional only so existing call sites keep compiling.
    /// </remarks>
    public static string ForEntityConfig(string entityTypeName, string? namespacePrefix = null)
        => string.IsNullOrEmpty(namespacePrefix) || namespacePrefix == "<global namespace>"
            ? $"EntityConfig.{entityTypeName}.g.cs"
            : $"EntityConfig.{namespacePrefix}.{entityTypeName}.g.cs";

    /// <summary>
    /// Host-level DbContext hint: <c>DbContext.Billing.g.cs</c>
    /// </summary>
    public static string ForDbContext(string boundaryName)
        => $"DbContext.{boundaryName}.g.cs";
}
