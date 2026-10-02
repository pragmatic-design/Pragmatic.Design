using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.FastEnum.Templates;

/// <summary>
///     Emits <c>_Infra.FastEnum.Registration.g.cs</c> — one entry point per assembly that adds every
///     generated <c>{Type}JsonConverter</c> to a <see cref="System.Text.Json.JsonSerializerOptions"/>.
///     <para>
///         The host calls it through the FastEnumConverters metadata aggregation. It takes the options
///         object rather than an <c>IServiceCollection</c> on purpose: a boundary library does not
///         reference ASP.NET, so it cannot name <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c>.
///     </para>
/// </summary>
internal sealed class FastEnumConverterRegistrationTemplate : CSharpTemplate
{
    private readonly string _namespace;
    private readonly IReadOnlyList<string> _converterTypes;

    /// <param name="ns">The assembly's root namespace.</param>
    /// <param name="converterTypes">Fully qualified converter type names, without the global:: prefix.</param>
    public FastEnumConverterRegistrationTemplate(string ns, IReadOnlyList<string> converterTypes)
    {
        _namespace = ns;
        _converterTypes = converterTypes;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/FastEnum";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("FastEnum", "Registration"),
        ToSourceText());

    protected override bool Validate() => _converterTypes.Count > 0;

    public override void RenderFile()
    {
        AppendLine($"namespace {GeneratedRegistrationNames.InGeneratedNamespace(_namespace)};");
        AppendLine();

        XmlSummary("Adds this assembly's generated [FastEnum] JSON converters to a JsonSerializerOptions.");
        AppendLine($"public static class {GeneratedRegistrationNames.FastEnumConvertersClass}");
        AppendLine("{");
        IncreaseIndent();

        XmlSummary("Adds every generated converter. Call before adding JsonStringEnumConverter: the first converter that accepts a type wins, and the framework one accepts every enum.");
        AppendLine($"public static void {GeneratedRegistrationNames.FastEnumConvertersMethod}(global::System.Text.Json.JsonSerializerOptions options)");
        AppendLine("{");
        IncreaseIndent();

        foreach (var converter in _converterTypes)
            AppendLine($"options.Converters.Add(new global::{converter}());");

        DecreaseIndent();
        AppendLine("}");

        DecreaseIndent();
        AppendLine("}");
    }
}
