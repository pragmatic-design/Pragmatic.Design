using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates the AccessScopes property, GrantScope/RevokeScope methods, and IScopedEntity interface
///     for entities with [HasAccessScopes]. Skips generation if the developer already declares AccessScopes.
/// </summary>
internal sealed class ScopedPropertyTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public ScopedPropertyTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"Scoping for {_model.TypeName}";
    protected override string? TriggerInfo => $"[HasAccessScopes] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Scoping", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() =>
        _model is { IsValid: true, IsScopedEntity: true, HasManualScopedEntityProps: false };

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Auto-generated scoping properties for {_model.TypeName}.");

        var interfaces = new List<string>();
        if (!_model.HasManualScopedEntityInterface)
            interfaces.Add("global::Pragmatic.Persistence.Entity.IScopedEntity");

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true },
            interfaces: interfaces);
    }

    private void RenderBody()
    {
        Comment("IScopedEntity");
        AppendLine("public global::System.Collections.Generic.List<string> AccessScopes { get; private set; } = [];");
        AppendLine();

        XmlSummary("Grants access to this entity for the given scope identifier.");
        XmlParam("scope", "The scope identifier to grant (e.g., \"user:alice\", \"role:manager\", \"scope:team-a\").");
        Method("GrantScope", () =>
        {
            AppendLine("if (!AccessScopes.Contains(scope))");
            IncreaseIndent();
            AppendLine("AccessScopes.Add(scope);");
            DecreaseIndent();
        }, "void", [new MethodParameter("string", "scope")], AccessModifier.Internal);
        AppendLine();

        XmlSummary("Revokes access to this entity for the given scope identifier.");
        XmlParam("scope", "The scope identifier to revoke.");
        Method("RevokeScope", () =>
        {
            AppendLine("AccessScopes.Remove(scope);");
        }, "void", [new MethodParameter("string", "scope")], AccessModifier.Internal);
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
