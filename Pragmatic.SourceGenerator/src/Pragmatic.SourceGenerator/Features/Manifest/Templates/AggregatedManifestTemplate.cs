using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     Emits <c>_Metadata.PragmaticManifest.Aggregated.g.cs</c>: every module manifest the host can
///     see, merged into one JSON constant, plus the module-load registration that makes the runtime
///     OpenAPI enrichment able to read it.
/// </summary>
/// <remarks>
///     <para>
///         The counterpart of <c>Features/Glossary/Templates/AsyncApiTemplate</c>: two documents
///         describing the two halves of the same published contract, now written the same way.
///     </para>
///     <para>
///         ⚠️ The XML summaries are written with <c>AppendLine</c> rather than <c>XmlSummary</c>, and
///         that is deliberate: <c>XmlSummary</c> renders the three-line form, and this file is the
///         application's published contract — the change that introduced this template was required to
///         leave the emitted bytes alone below the header. One-line summaries are what it emitted.
///     </para>
/// </remarks>
internal sealed class AggregatedManifestTemplate : CSharpTemplate
{
    private readonly string _assemblyName;
    private readonly string _json;
    private readonly int _moduleCount;
    private readonly bool _hasManifestRegistry;
    private readonly string _namespace;

    /// <param name="assemblyName">The host assembly, which names both the namespace and the document.</param>
    /// <param name="json">The merged manifest, embedded verbatim as a raw string literal.</param>
    /// <param name="moduleCount">How many module manifests were merged.</param>
    /// <param name="hasManifestRegistry">
    ///     Whether the host references <c>Pragmatic.Endpoints.Manifest.ManifestRegistry</c>. The caller
    ///     answers this from the compilation: the generated code may only name a type the host actually
    ///     references, and without the registration <c>ManifestReader.ReadAll()</c> is empty.
    /// </param>
    public AggregatedManifestTemplate(
        string assemblyName,
        string json,
        int moduleCount,
        bool hasManifestRegistry)
    {
        _assemblyName = assemblyName;
        _json = json;
        _moduleCount = moduleCount;
        _hasManifestRegistry = hasManifestRegistry;
        _namespace = assemblyName.Replace("-", "_");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Manifest";

    /// <summary>The header line: the host and how many modules.</summary>
    protected override string? SourceInfo => $"Aggregated API manifest for {_assemblyName} — {_moduleCount} module(s)";

    /// <remarks>
    ///     No module is a valid manifest: a host whose modules publish nothing yet still registers it,
    ///     and the registration names this class.
    /// </remarks>
    protected override bool Validate() => !string.IsNullOrEmpty(_json);

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForMetadata("PragmaticManifest.Aggregated"), ToSourceText());

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();
        AppendLine("/// <summary>Aggregated API manifest for all modules in this host.</summary>");
        AppendLine("internal static class PragmaticManifest");
        AppendLine("{");
        AppendLine("    /// <summary>Aggregated manifest JSON.</summary>");
        AppendLine("    internal const string Json = \"\"\"");

        // The closing delimiter of a raw string literal sets the indentation stripped from every line
        // inside it. At column 0 nothing is stripped, which is what keeps the JSON exactly as the
        // builder produced it — indent the delimiter and every line of the document shifts with it.
        AppendLine(_json);
        AppendLine("\"\"\";");

        AppendLine();
        AppendLine("    /// <summary>Number of modules.</summary>");
        AppendLine($"    internal const int ModuleCount = {_moduleCount};");

        if (_hasManifestRegistry)
        {
            AppendLine();
            AppendLine("    /// <summary>Registers the aggregated manifest at module load time.</summary>");
            AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            AppendLine("    internal static void RegisterManifest()");
            AppendLine("    {");
            AppendLine("        global::Pragmatic.Endpoints.Manifest.ManifestRegistry.Register(Json);");
            AppendLine("    }");
        }

        AppendLine("}");
    }
}
