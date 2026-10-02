using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     Emits the compile-time OpenAPI 3.1 document as <c>PragmaticOpenApi.Json</c>, built by
///     <see cref="OpenApiJsonGenerator" /> from the same module manifests the aggregated manifest
///     merges.
/// </summary>
/// <remarks>
///     ⚠️ The hint moved from the hand-written <c>_Infra.OpenApi.g.cs</c> to
///     <c>VirtualFolderHints.ForAssembly</c>, which spells an infra hint with a category and an
///     artifact — so the file is now <c>_Infra.OpenApi.Generated.g.cs</c>, the same shape as
///     <c>_Infra.AsyncApi.Generated.g.cs</c> beside it. Nothing reads a generated file by name; the
///     two test suites that asserted the old string were updated with it.
/// </remarks>
internal sealed class OpenApiDocumentTemplate : CSharpTemplate
{
    private readonly string _assemblyName;
    private readonly string _json;
    private readonly bool _requiresAuthentication;
    private readonly bool _hasOpenApiRegistry;
    private readonly string _namespace;

    /// <param name="assemblyName">The host assembly, which names the namespace.</param>
    /// <param name="json">The OpenAPI 3.1 document, embedded verbatim as a raw string literal.</param>
    /// <param name="requiresAuthentication">Passed to the registry so the served document can say so.</param>
    /// <param name="hasOpenApiRegistry">
    ///     Whether the host references <c>Pragmatic.Endpoints.OpenApi.PragmaticOpenApiRegistry</c>.
    ///     Without it the document is still emitted as a constant and simply registers nowhere.
    /// </param>
    public OpenApiDocumentTemplate(
        string assemblyName,
        string json,
        bool requiresAuthentication,
        bool hasOpenApiRegistry)
    {
        _assemblyName = assemblyName;
        _json = json;
        _requiresAuthentication = requiresAuthentication;
        _hasOpenApiRegistry = hasOpenApiRegistry;
        _namespace = assemblyName.Replace("-", "_");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/OpenApi";

    /// <summary>The header line: the assembly the document describes.</summary>
    protected override string? SourceInfo => $"Compile-time OpenAPI 3.1 for {_assemblyName}";

    protected override bool Validate() => !string.IsNullOrEmpty(_json);

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("OpenApi", "Generated"), ToSourceText());

    public override void RenderFile()
    {
        if (_hasOpenApiRegistry)
            AddUsing("System.Runtime.CompilerServices");

        AppendLine($"namespace {_namespace};");
        AppendLine();
        AppendLine("/// <summary>OpenAPI 3.1 document generated at compile time from Pragmatic Manifest.</summary>");
        AppendLine("internal static class PragmaticOpenApi");
        AppendLine("{");
        AppendLine("    /// <summary>OpenAPI 3.1 JSON.</summary>");
        AppendLine("    internal const string Json = \"\"\"");

        // Column 0 for the closing delimiter: see AggregatedManifestTemplate for why the JSON would
        // otherwise be re-indented by the compiler.
        AppendLine(_json);
        AppendLine("\"\"\";");

        if (_hasOpenApiRegistry)
        {
            AppendLine();
            AppendLine("    /// <summary>");
            AppendLine("    ///     Whether at least one of THIS host's operations requires authentication.");
            AppendLine("    /// </summary>");
            AppendLine("    /// <remarks>");
            AppendLine("    ///     ⚠️ A constant beside the document because it is a fact about the same host, and");
            AppendLine("    ///     the generated composition registers the two together. Read from the process-wide");
            AppendLine("    ///     registry it would be whichever host loaded last, so a two-host process");
            AppendLine("    ///     would publish one host's security requirement on the other's contract.");
            AppendLine("    /// </remarks>");
            AppendLine($"    internal const bool RequiresAuthentication = {(_requiresAuthentication ? "true" : "false")};");
        }

        if (_hasOpenApiRegistry)
        {
            AppendLine();
            AppendLine("    /// <summary>Registers the compile-time OpenAPI spec at module load time.</summary>");
            AppendLine("    [ModuleInitializer]");
            AppendLine("    internal static void RegisterOpenApi()");
            AppendLine("    {");
            AppendLine("        global::Pragmatic.Endpoints.OpenApi.PragmaticOpenApiRegistry.Register(");
            AppendLine($"            Json, requiresAuthentication: {(_requiresAuthentication ? "true" : "false")});");
            AppendLine("    }");
        }

        AppendLine("}");
    }
}
