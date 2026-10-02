using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating Set{Property} methods with change tracking for properties
///     with private setters. Each setter records the property name in _modifiedProperties
///     when the value actually changes, enabling selective entity validation.
///     When the entity is a cascade source, setters for cascade properties also emit
///     <c>EntityPropertyChanged</c> domain events.
/// </summary>
internal sealed class EntitySettersTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;
    private readonly ImmutableHashSet<string> _cascadeSourceProperties;

    public EntitySettersTemplate(EntityMetadataModel model, ImmutableHashSet<string>? cascadeSourceProperties = null)
    {
        _model = model;
        _cascadeSourceProperties = cascadeSourceProperties ?? ImmutableHashSet<string>.Empty;
    }

    private bool HasCascadeSourceProperties => !_cascadeSourceProperties.IsEmpty;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Setters", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, HasPrivateSetterProperties: true };
    }

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary(
            $"Internal setters with change tracking for {_model.TypeName} properties with private setters.");

        var interfaces = new List<string> { "global::Pragmatic.Persistence.Entity.IChangeTracking" };
        if (HasCascadeSourceProperties)
            interfaces.Add("global::Pragmatic.Events.IHasDomainEvents");

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true },
            interfaces: interfaces);
    }

    private void RenderBody()
    {
        RenderChangeTrackingFields();
        AppendLine();

        if (HasCascadeSourceProperties)
        {
            RenderDomainEventsFields();
            AppendLine();
        }

        AppendLine("#region Internal Setters (with change tracking)");
        AppendLine();

        var props = _model.PrivateSetterProperties.ToList();
        for (var i = 0; i < props.Count; i++)
        {
            var isCascadeSource = _cascadeSourceProperties.Contains(props[i].Name);
            RenderSetterMethod(props[i], isCascadeSource);
            if (i < props.Count - 1)
                AppendLine();
        }

        AppendLine();
        AppendLine("#endregion");
    }

    private void RenderChangeTrackingFields()
    {
        AppendLine("#region Change Tracking (IChangeTracking)");
        AppendLine();

        AppendLine(
            "private readonly global::System.Collections.Generic.HashSet<string> _modifiedProperties = [];");
        AppendLine(
            "private readonly global::System.Collections.Generic.HashSet<string> _collectionsModified = [];");
        AppendLine("private bool _isNew;");
        AppendLine();

        AppendLine("/// <inheritdoc/>");
        AppendLine("[global::System.Text.Json.Serialization.JsonIgnore]");
        AppendLine(
            "public global::System.Collections.Generic.IReadOnlySet<string> ModifiedProperties => _modifiedProperties;");
        AppendLine();

        AppendLine("/// <inheritdoc/>");
        AppendLine("[global::System.Text.Json.Serialization.JsonIgnore]");
        AppendLine(
            "public global::System.Collections.Generic.IReadOnlySet<string> CollectionsModified => _collectionsModified;");
        AppendLine();

        AppendLine("/// <inheritdoc/>");
        Method("ResetModifiedProperties", () =>
        {
            AppendLine("_modifiedProperties.Clear();");
            AppendLine("_collectionsModified.Clear();");
        }, "void", [], AccessModifier.Public);
        AppendLine();

        AppendLine("/// <inheritdoc/>");
        AppendLine("[global::System.Text.Json.Serialization.JsonIgnore]");
        AppendLine("[global::System.ComponentModel.DataAnnotations.Schema.NotMapped]");
        PropertyWithGetter("IsNew", "bool", () =>
        {
            AppendLine("get => _isNew;");
            AppendLine("set => _isNew = value;");
        }, AccessModifier.Public);

        AppendLine();
        AppendLine("#endregion");
    }

    private void RenderDomainEventsFields()
    {
        AppendLine("#region Domain Events (IHasDomainEvents for cascade source)");
        AppendLine();
        AppendLine(
            "private readonly global::System.Collections.Generic.List<global::Pragmatic.Events.IDomainEvent> _domainEvents = [];");
        AppendLine();

        AppendLine("/// <inheritdoc/>");
        AppendLine("[global::System.ComponentModel.DataAnnotations.Schema.NotMapped]");
        AppendLine(
            "public global::System.Collections.Generic.IReadOnlyList<global::Pragmatic.Events.IDomainEvent> DomainEvents => _domainEvents;");
        AppendLine();

        AppendLine("/// <inheritdoc/>");
        Method("ClearDomainEvents", () =>
        {
            AppendLine("_domainEvents.Clear();");
        }, "void", [], AccessModifier.Public);

        AppendLine();
        AppendLine("#endregion");
    }

    private void RenderSetterMethod(PropertyMetadataModel prop, bool isCascadeSource = false)
    {
        XmlSummary($"Sets the {prop.Name} property with change tracking.");
        XmlParam("value", $"The new value for {prop.Name}.");

        var methodName = $"Set{prop.Name}";
        // Only prefix global:: for namespace-qualified types (not C# keyword aliases like string, int)
        var qualifiedType = prop.TypeName.Contains('.') ? $"global::{prop.TypeName}" : prop.TypeName;
        var parameters = new List<MethodParameter>
        {
            new(qualifiedType, "value")
        };

        // AggressiveInlining for simple setters (non-cascade); cascade setters are too large to inline
        var inlineAttr = isCascadeSource
            ? null
            : "global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)";

        Method(methodName, () =>
        {
            // Equality check: skip assignment if value hasn't changed
            AppendLine(
                $"if (global::System.Collections.Generic.EqualityComparer<{qualifiedType}>.Default.Equals({prop.Name}, value))");
            Block(() => AppendLine("return;"));
            AppendLine();

            if (isCascadeSource)
            {
                // Capture old value before assignment for the domain event
                AppendLine($"var oldValue = {prop.Name};");
            }

            AppendLine($"{prop.Name} = value;");
            AppendLine($"_modifiedProperties.Add(nameof({prop.Name}));");

            if (isCascadeSource)
            {
                AppendLine();
                Comment("Raise domain event for cascade propagation");
                AppendLine(
                    $"_domainEvents.Add(global::Pragmatic.Events.EntityPropertyChanged<global::{_model.FullTypeName}>.Create(Id, nameof({prop.Name}), oldValue, value));");
            }
        }, "void", parameters, AccessModifier.Internal, attribute: inlineAttr);
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
