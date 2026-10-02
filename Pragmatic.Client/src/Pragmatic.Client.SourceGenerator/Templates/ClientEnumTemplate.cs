using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>
///     Emits an enum declared in the manifest, so a typed member survives into the client instead of
///     degrading to <c>object</c>.
/// </summary>
/// <remarks>
///     Members are emitted verbatim, so the caller must have filtered out anything that is not a valid C#
///     identifier: one bad name would make the whole file uncompilable, taking every other type with it.
/// </remarks>
internal sealed class ClientEnumTemplate(
    string clientNamespace,
    string enumName,
    IReadOnlyList<string> members) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    public override Artifact RenderOutput() => new($"{enumName}.g.cs", ToSourceText());

    protected override bool Validate() => enumName.Length > 0 && members.Count > 0;

    public override void RenderFile()
    {
        AppendNamespace(clientNamespace);
        AppendLine();

        // CSharpTemplate has no Enum() primitive; the shape is small enough to write directly.
        AppendLine($"public enum {enumName}");
        AppendLine("{");
        IncreaseIndent();
        for (var i = 0; i < members.Count; i++)
            AppendLine(i < members.Count - 1 ? $"{members[i]}," : members[i]);
        DecreaseIndent();
        AppendLine("}");
    }
}
