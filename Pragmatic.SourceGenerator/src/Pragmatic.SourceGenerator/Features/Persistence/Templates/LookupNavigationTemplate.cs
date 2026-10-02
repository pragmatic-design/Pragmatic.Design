using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a navigation property on a consumer entity that resolves a [Lookup] entity
///     from the ambient LookupResolver. The FK property ({LookupType}Id) is auto-detected.
/// </summary>
internal sealed class LookupNavigationTemplate : CSharpTemplate
{
    private readonly LookupConsumerModel _consumer;

    public LookupNavigationTemplate(LookupConsumerModel consumer) => _consumer = consumer;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/LookupNavigation";
    protected override string? SourceInfo => $"LookupNavigation for {_consumer.TypeName} → {_consumer.Lookup.TypeName}";
    protected override string? TriggerInfo => $"[Lookup] on {_consumer.Lookup.TypeName}, FK {_consumer.FkPropertyName} on {_consumer.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_consumer.TypeName,
            NamingHelper.AppendSuffix(_consumer.Lookup.TypeName, "Navigation"), _consumer.Namespace),
        ToSourceText());

    protected override bool Validate() =>
        !string.IsNullOrEmpty(_consumer.TypeName) &&
        !string.IsNullOrEmpty(_consumer.FkPropertyName) &&
        _consumer.Lookup.IsValid;

    public override void RenderFile()
    {
        AddUsing("System.ComponentModel.DataAnnotations.Schema");

        AppendNamespace(_consumer.Namespace);
        AppendLine();

        Class(_consumer.TypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var lookupType = $"global::{_consumer.Lookup.FullTypeName}";
        var idType = $"global::{_consumer.Lookup.IdType}";

        if (_consumer.IsFkNullable)
        {
            // Nullable FK → nullable navigation. Resolve via TryGet so a non-null FK pointing at an id
            // that is absent from the cache yields null instead of a KeyNotFoundException.
            XmlSummary(
                $"Resolved from lookup cache. Backed by <see cref=\"{_consumer.FkPropertyName}\"/>. Null when FK is null or the id is not cached.");
            AppendLine("[NotMapped]");
            ExpressionProperty(
                _consumer.Lookup.TypeName,
                $"{lookupType}?",
                $"{_consumer.FkPropertyName} is {{}} __fk && global::Pragmatic.Persistence.Entity.LookupResolver.TryGet<{lookupType}, {idType}>(__fk, out var __v) ? __v : null");
        }
        else
        {
            XmlSummary(
                $"Resolved from lookup cache. Backed by <see cref=\"{_consumer.FkPropertyName}\"/>.");
            AppendLine("[NotMapped]");
            ExpressionProperty(
                _consumer.Lookup.TypeName,
                lookupType,
                $"global::Pragmatic.Persistence.Entity.LookupResolver.Get<{lookupType}, {idType}>({_consumer.FkPropertyName})");
        }
    }
}
