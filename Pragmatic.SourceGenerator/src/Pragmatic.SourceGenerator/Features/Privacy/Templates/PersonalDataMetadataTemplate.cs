using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
/// Emits <c>[assembly: PragmaticMetadata(MetadataCategory.PersonalData, …)]</c> describing every
/// classified property in this assembly, so the host can assemble the Article 30 processing register
/// across all referenced modules.
/// </summary>
/// <remarks>
/// This is the half of the register that can be derived. What is processed, in what category, how it is
/// erased and what is retained and why all follow from the code. The other half — who the controller is,
/// and for what purpose — cannot be read from anywhere and stays configuration; a generator that
/// invented it would be producing a document that reads as authoritative and is not.
///
/// The value in generating it at all is the direction of truth: a register maintained by hand describes
/// the system as someone remembered it, and drifts from the first column added afterwards. This one is a
/// projection of what is actually there, recomputed at every build.
/// </remarks>
internal sealed class PersonalDataMetadataTemplate : CSharpTemplate
{
    private readonly IReadOnlyList<PrivacyEntityModel> _entities;
    private readonly string? _registrationMethodFqn;

    /// <param name="entities">The entities whose classifications this assembly declares.</param>
    /// <param name="registrationMethodFqn">
    ///     The generated <c>Add*</c> a host has to call to wire this assembly's adapters, or null when
    ///     none was generated — the module classifies data but does not reference the Privacy runtime.
    /// </param>
    public PersonalDataMetadataTemplate(
        IReadOnlyList<PrivacyEntityModel> entities, string? registrationMethodFqn = null)
    {
        _entities = entities;
        _registrationMethodFqn = registrationMethodFqn;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Privacy";

    protected override bool Validate() => _entities.Count > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForMetadata("PersonalData"), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        // Built through the shared builder rather than by hand: a retention reason is free text a
        // developer wrote, so it can contain a quote or a backslash — which would end the JSON string
        // early and corrupt the whole payload, not just one field.
        var json = new MetadataJsonBuilder();

        // The standard envelope, not a bare array. "registrationMethod" is the channel the host reads to
        // learn which generated Add* wires this assembly's adapters; an assembly that describes its
        // personal data and never says who registers the code acting on it is the gap this closes.
        json.StartObject()
            .Property("generator", "Pragmatic.SourceGenerator/Privacy")
            .Property("registrationMethod", _registrationMethodFqn ?? string.Empty)
            .Property("data");

        json.StartObject().Property("entities");
        json.StartArray();

        foreach (var entity in _entities)
        {
            if (!entity.HasPersonalData)
                continue;

            json.StartObject()
                .Property("type", entity.FullTypeName)
                .Property("namespace", entity.Namespace)
                .Property("isSubject", entity.IsSubject)
                .Property("properties");

            json.StartArray();

            foreach (var p in entity.Properties)
            {
                if (p.Classification is not { } c)
                    continue;

                json.StartObject()
                    .Property("name", p.Name)
                    .Property("category", c.Category)
                    .Property("erasure", c.Erasure)
                    .Property("encrypted", c.Encrypted)
                    .Property("reason", c.Reason ?? string.Empty)
                    .EndObject();
            }

            json.EndArray();
            json.EndObject();
        }

        json.EndArray();
        json.EndObject();
        json.EndObject();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.PersonalData, \"{MetadataSchemaVersions.PersonalData}\", " +
            $"\"\"\"{json.ToEscapedString()}\"\"\")]");
    }
}
