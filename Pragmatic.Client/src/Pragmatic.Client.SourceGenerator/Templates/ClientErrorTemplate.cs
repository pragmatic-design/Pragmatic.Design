using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>
///     Emits a typed error record implementing <c>Pragmatic.Result.IError</c>, with the extension properties
///     the manifest declares for it (the domain context carried in a ProblemDetails response).
/// </summary>
internal sealed class ClientErrorTemplate(
    string clientNamespace,
    string typeName,
    string errorCode,
    int statusCode,
    IReadOnlyList<ClientDtoProperty> extensions) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    public override Artifact RenderOutput() => new($"{typeName}.g.cs", ToSourceText());

    protected override bool Validate() => typeName.Length > 0;

    public override void RenderFile()
    {
        AppendNamespace(clientNamespace);
        AppendLine();

        Record(typeName, RenderBody,
            interfaces: ["Pragmatic.Result.IError"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        AppendLine($"public string Code => \"{errorCode}\";");
        AppendLine($"public int StatusCode => {statusCode};");
        AppendLine($"public string Title => \"{typeName}\";");

        foreach (var extension in extensions)
            AppendLine($"public {extension.Type}? {extension.Name} {{ get; init; }}");
    }
}
