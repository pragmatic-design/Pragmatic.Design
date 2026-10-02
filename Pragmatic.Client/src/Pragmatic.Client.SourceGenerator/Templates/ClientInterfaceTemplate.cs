using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>
///     Emits <c>I{Boundary}Client</c>: one method per endpoint, returning <c>Result&lt;T, IError&gt;</c> —
///     or <c>VoidResult&lt;IError&gt;</c> — so a failure is a value, never an exception.
/// </summary>
internal sealed class ClientInterfaceTemplate(
    string clientNamespace,
    string boundary,
    List<PragmaticClientGenerator.EndpointDto> endpoints) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    protected override string? SourceInfo => $"API manifest for {boundary}";

    public override Artifact RenderOutput() => new($"I{boundary}Client.g.cs", ToSourceText());

    protected override bool Validate() => endpoints.Count > 0;

    public override void RenderFile()
    {
        AddUsing("Pragmatic");
        AppendNamespace(clientNamespace);
        AppendLine();

        AppendLine($"public interface I{boundary}Client");
        AppendLine("{");
        IncreaseIndent();

        foreach (var endpoint in endpoints)
        {
            var method = PragmaticClientGenerator.MethodName(endpoint);
            var returnType = PragmaticClientGenerator.ReturnType(endpoint);

            if (endpoint.Summary is not null)
                AppendLine($"/// <summary>{PragmaticClientGenerator.Escape(endpoint.Summary)}</summary>");

            AppendLine($"Task<{returnType}> {method}({PragmaticClientGenerator.BuildParams(endpoint)}CancellationToken ct = default);");
            AppendLine();
        }

        DecreaseIndent();
        AppendLine("}");
    }
}
