using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>What the document generator reports at the call site or the type, with the remedy.</summary>
internal static class DocDiagnostics
{
    private const string Category = "Pragmatic.Persistence.Documents";

    public static readonly DiagnosticDescriptor Unsupported = new(
        "PRAG3101",
        "Not translated to a document query",
        "{0} is not translated: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Captured = new(
        "PRAG3102",
        "A lambda reads a value of the enclosing method",
        "The lambda reads '{0}', a value of the enclosing method: pass it as the argument of the two-parameter form, Where((o, a) => o.Field == a, {0})",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotPartial = new(
        "PRAG3103",
        "A document type is not partial",
        "'{0}' must be partial: the generator adds its document reader and writer to it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMember = new(
        "PRAG3104",
        "A property is not a document field",
        "'{0}.{1}' ({2}) is not a document field: {3}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotRooted = new(
        "PRAG3105",
        "A chain does not start at session.Query<T>()",
        "'{0}' is called on a chain the generator cannot follow back to session.Query<T>() (a chain handed over by a method or a field): write the chain where it starts, or it throws at run time",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Information (it does not fail a build on MongoDB): on Cosmos DB the chain runs in one partition only.</summary>
    public static readonly DiagnosticDescriptor CosmosNeedsPartition = new(
        "PRAG3106",
        "On Cosmos DB the chain needs the partition key",
        "On Cosmos DB, {0} with {1} runs in one partition only (the gateway does not serve it across partitions): the chain does not name {2} in an equality, so it throws there",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TooManyTracked = new(
        "PRAG3107",
        "A Pragmatic entity has more fields than a save tracks",
        "'{0}' has {1} document fields besides its key and version: a unit of work tracks up to 64 (one bit each); move some into an embedded document",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A warning, as the Sql engine's PRAG3033: EF Core runs the query as before; the documents engine refuses it at run time.</summary>
    public static readonly DiagnosticDescriptor QueryNotCompiled = new(
        "PRAG3108",
        "A declared query is not compiled for the documents engine",
        "{0} is not compiled for the documents engine: {1}. IReadRepository.RunAsync refuses it on the documents engine; EF Core runs it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>A warning, as the Sql engine's PRAG3034: a grid adapter compiled without a part of its declaration.</summary>
    public static readonly DiagnosticDescriptor GridPartlyCompiled = new(
        "PRAG3109",
        "A grid adapter is compiled without a part of its declaration",
        "{0} is compiled for the documents engine without {1}. EF Core runs it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>A warning, as the Sql engine's PRAG3035: EF Core runs the rule as before; registered and computed, the documents engine refuses it at read.</summary>
    public static readonly DiagnosticDescriptor ScopeRuleNotCompiled = new(
        "PRAG3110",
        "A data scope rule of a Pragmatic entity is not compiled for the documents engine",
        "The data scope rule {0} is not compiled for the documents engine: {1}. Registered and Computed or Hybrid, it is refused at read; EF Core runs it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>Information: the call site's query as the generator wrote it (shown with -p:PragmaticCensus=true promoted, or in the IDE).</summary>
    public static readonly DiagnosticDescriptor Translated = new(
        "PRAG3199",
        "Translated to a document query",
        "{0}: {1}",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true);
}
