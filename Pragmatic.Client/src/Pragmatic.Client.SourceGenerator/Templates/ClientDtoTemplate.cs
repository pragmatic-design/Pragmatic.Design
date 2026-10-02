using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>A property of a generated DTO: its C# type, its name, and whether it needs a null-forgiving initializer.</summary>
internal sealed record ClientDtoProperty(string Type, string Name, bool NeedsDefaultInitializer);

/// <summary>
///     Emits a record DTO — a request body, or a response payload projected from the manifest's type table.
/// </summary>
internal sealed class ClientDtoTemplate(
    string clientNamespace,
    string typeName,
    IReadOnlyList<ClientDtoProperty> properties,
    string? summary = null) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    public override Artifact RenderOutput() => new($"{typeName}.g.cs", ToSourceText());

    protected override bool Validate() => typeName.Length > 0 && properties.Count > 0;

    public override void RenderFile()
    {
        AppendNamespace(clientNamespace);
        AppendLine();

        if (summary is not null)
            XmlSummary(summary);

        Record(typeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        foreach (var property in properties)
        {
            // A non-nullable property is filled by the deserializer; default! keeps the contract without
            // tripping CS8618 on the generated DTO.
            var initializer = property.NeedsDefaultInitializer ? " = default!;" : "";
            AppendLine($"public {property.Type} {property.Name} {{ get; init; }}{initializer}");
        }
    }
}
