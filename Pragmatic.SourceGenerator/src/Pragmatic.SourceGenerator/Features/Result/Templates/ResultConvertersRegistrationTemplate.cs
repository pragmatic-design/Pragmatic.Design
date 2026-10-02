using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Templates;

/// <summary>
///     One entry point per assembly that registers every declared result converter.
/// </summary>
/// <remarks>
///     Mirrors how a generated JSON context is wired — the host writes one line — instead of asking for
///     a line per result type. What it replaces is <c>options.Converters.Add(new ResultJsonConverterFactory())</c>,
///     a single registration that then decided at run time, per type, which converter to build.
/// </remarks>
internal sealed class ResultConvertersRegistrationTemplate : CSharpTemplate
{
    private const string ClassName = "PragmaticResultConverters";
    private const string MethodName = "AddPragmaticResultConverters";

    private readonly EquatableArray<ResultContractModel> _contracts;
    private readonly string _namespace;

    public ResultConvertersRegistrationTemplate(EquatableArray<ResultContractModel> contracts, string generatedNamespace)
    {
        _contracts = contracts;
        _namespace = generatedNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Result";
    protected override string? TriggerInfo => $"{_contracts.Length} [JsonResultContract<…>] declaration(s)";

    protected override bool Validate() => _contracts.Length > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Result", "ResultConverters"), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("Registers the JSON converters for the result types this assembly declares.");
        Class(ClassName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        XmlSummary("Adds every declared result converter to the shared options.");
        XmlParam("json", "The Pragmatic JSON options.");
        XmlReturns("The options, for chaining.");

        Method(MethodName, RenderRegistrations,
            "global::Pragmatic.Serialization.PragmaticJsonOptions",
            [
                new MethodParameter("global::Pragmatic.Serialization.PragmaticJsonOptions", "json")
                {
                    IsExtension = true
                }
            ],
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderRegistrations()
    {
        AppendLine("global::System.ArgumentNullException.ThrowIfNull(json);");
        AppendLine();

        foreach (var contract in _contracts)
            AppendLine($"json.AddConverter({contract.NewConverterExpression});");

        AppendLine();
        AppendLine("return json;");
    }
}
