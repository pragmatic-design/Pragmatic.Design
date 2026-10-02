using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
///     Generates the assembly's <c>IProcessingActivitySource</c> — what this module contributes to the
///     Article 30 register.
/// </summary>
/// <remarks>
///     <para>
///         The same facts <c>PersonalDataMetadataTemplate</c> writes into
///         <c>[assembly: PragmaticMetadata]</c>, but as typed objects the runtime can consume without
///         parsing anything. The attribute is for a host reading a <em>referenced</em> assembly's
///         declarations; this is for the register the application actually builds.
///     </para>
///     <para>
///         No database and no EF Core: these are declarations about the code, not rows. It is therefore
///         emitted wherever the Privacy runtime is referenced, even in a module with no persistence at
///         all — which is what stops the register from silently omitting such a module.
///     </para>
/// </remarks>
internal sealed class ProcessingActivitySourceTemplate : CSharpTemplate
{
    /// <summary>The type name, shared with the registration that names it.</summary>
    public const string ClassName = "PragmaticProcessingActivitySource";

    private readonly IReadOnlyList<PrivacyEntityModel> _entities;
    private readonly IReadOnlyList<Endpoints.Models.EndpointModel> _endpoints;
    private readonly string _namespace;

    public ProcessingActivitySourceTemplate(
        IReadOnlyList<PrivacyEntityModel> entities,
        IReadOnlyList<Endpoints.Models.EndpointModel> endpoints,
        string @namespace)
    {
        _entities = entities;
        _endpoints = endpoints;
        _namespace = @namespace;
    }

    /// <summary>The <c>Namespace.Class</c> a registration has to name.</summary>
    public static string FqnFor(string @namespace)
        => string.IsNullOrEmpty(@namespace) ? ClassName : @namespace + "." + ClassName;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Privacy";

    protected override bool Validate() => Classified().Count > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Privacy", "ProcessingActivities"), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");

        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary(
            "SG-generated processing activities for this assembly, derived from the [PersonalData] " +
            "classifications in it.");

        Class(ClassName, RenderBody,
            interfaces: ["global::Pragmatic.Privacy.IProcessingActivitySource"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlSummary("The activities, fixed at compile time — they describe declarations, not state.");
        AppendLine("private static readonly IReadOnlyList<global::Pragmatic.Privacy.ProcessingActivity> Declared =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var entity in Classified())
            RenderActivity(entity);

        DecreaseIndent();
        AppendLine("];");
        AppendLine();

        XmlInheritDoc();
        AppendLine(
            "public global::System.Threading.Tasks.ValueTask<IReadOnlyList<global::Pragmatic.Privacy.ProcessingActivity>> " +
            "GetActivitiesAsync(global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        AppendLine("=> new(Declared);");
        DecreaseIndent();
        AppendLine();

        RenderOperations();
    }

    /// <summary>
    ///     The operations through which this assembly's personal data is processed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A query and a mutation each name the entity they work on — <c>QueryEntityType</c> and
    ///         <c>MutationEntityType</c> — so the link from an operation to the personal data it touches
    ///         is a fact the compilation already holds. A domain action names none, and is read off the
    ///         dependencies it declares instead: see <c>EndpointTransform.ParseDomainActionEntities</c>.
    ///     </para>
    ///     <para>
    ///         A domain action counts as a <c>Write</c>. Pragmatic's taxonomy makes a query a query, so
    ///         an action is not one; and where the direction cannot be derived, the assumption that
    ///         overstates is the safe one in a document about what is done to people's data.
    ///     </para>
    ///     <para>
    ///         An operation on an unclassified entity is not listed: with no <c>[PersonalData]</c> on it
    ///         there is nothing for Article 30 to record.
    ///     </para>
    /// </remarks>
    private void RenderOperations()
    {
        var classified = Classified().ToDictionary(e => e.FullTypeName, e => e);

        XmlSummary("The operations that process it, derived from the queries and mutations declared here.");
        AppendLine(
            "private static readonly IReadOnlyList<global::Pragmatic.Privacy.ProcessingOperation> DeclaredOperations =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var endpoint in _endpoints.Where(e => e.IsValid).OrderBy(e => e.TypeName, System.StringComparer.Ordinal))
        foreach (var entityType in EntitiesOf(endpoint))
        {
            var key = entityType.StartsWith("global::", System.StringComparison.Ordinal)
                ? entityType.Substring("global::".Length)
                : entityType;

            if (!classified.TryGetValue(key, out var entity))
                continue;

            var access = endpoint.IsQuery ? "Read" : "Write";
            var fullName = string.IsNullOrEmpty(endpoint.Namespace)
                ? endpoint.TypeName
                : endpoint.Namespace + "." + endpoint.TypeName;

            AppendLine("new global::Pragmatic.Privacy.ProcessingOperation(");
            IncreaseIndent();
            AppendLine($"\"{fullName}\",");
            AppendLine($"global::Pragmatic.Privacy.ProcessingAccess.{access},");
            AppendLine($"\"{key}\",");
            AppendLine($"[{string.Join(", ", Categories(entity).Select(c => $"\"{c}\""))}],");
            AppendLine($"\"{StringHelper.CSharpLiteral(endpoint.Route ?? string.Empty)}\",");
            // What the handler was actually able to emit, not what the attribute asked for: a query
            // declaring [RecordAccess] in a module without the audit package records nothing, and a
            // register saying otherwise would be the one wrong row that discredits the rest.
            AppendLine(endpoint.CanRecordAccess ? "true" : "false");
            DecreaseIndent();
            AppendLine("),");
        }

        DecreaseIndent();
        AppendLine("];");
        AppendLine();

        XmlInheritDoc();
        AppendLine(
            "public global::System.Threading.Tasks.ValueTask<IReadOnlyList<global::Pragmatic.Privacy.ProcessingOperation>> " +
            "GetOperationsAsync(global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        AppendLine("=> new(DeclaredOperations);");
        DecreaseIndent();
    }

    /// <summary>The entities one operation touches — one for a query, several for an action or a mutation.</summary>
    /// <remarks>
    ///     A query and a mutation each name theirs in their own declaration. A domain action names none
    ///     and is read off its dependencies and its loads instead, which can yield several: an action that
    ///     composes three mutations processes three entities, and listing only the first would understate
    ///     exactly the operation the register most needs to describe. A mutation processes what it
    ///     preloads besides its own row as well.
    /// </remarks>
    private static IEnumerable<string> EntitiesOf(Endpoints.Models.EndpointModel endpoint)
    {
        if (endpoint.IsDomainAction)
            return endpoint.DomainActionEntityTypes;

        var single = endpoint.IsQuery ? endpoint.QueryEntityType : endpoint.MutationEntityType;
        if (single is null)
            return [];

        return endpoint.IsMutation
            ? new[] { single }.Concat(endpoint.InferredEntityTypes.Where(e => e != single))
            : [single];
    }

    private void RenderActivity(PrivacyEntityModel entity)
    {
        AppendLine("new global::Pragmatic.Privacy.ProcessingActivity(");
        IncreaseIndent();
        AppendLine($"\"{entity.FullTypeName}\",");
        AppendLine($"[{string.Join(", ", Categories(entity).Select(c => $"\"{c}\""))}],");
        AppendLine($"{(entity.IsSubject ? "true" : "false")},");
        RenderErasureMap(entity);
        RenderRetained(entity);
        DecreaseIndent();
        AppendLine("),");
    }

    private void RenderErasureMap(PrivacyEntityModel entity)
    {
        AppendLine("new Dictionary<string, string>(global::System.StringComparer.Ordinal)");
        AppendLine("{");
        IncreaseIndent();

        foreach (var property in entity.Properties)
            if (property.Classification is { } classification)
                AppendLine($"[\"{property.Name}\"] = \"{classification.Erasure}\",");

        DecreaseIndent();
        AppendLine("},");
    }

    private void RenderRetained(PrivacyEntityModel entity)
    {
        AppendLine("[");
        IncreaseIndent();

        foreach (var property in entity.Properties)
        {
            if (property.Classification is not { IsRetained: true } classification)
                continue;

            var reason = StringHelper.CSharpLiteral(classification.Reason ?? string.Empty);
            AppendLine(
                $"new global::Pragmatic.Privacy.RetainedItem(\"{entity.TypeName}.{property.Name}\", \"{reason}\"),");
        }

        DecreaseIndent();
        AppendLine("]");
    }

    /// <summary>The distinct categories declared on an entity, in a stable order.</summary>
    private static IReadOnlyList<string> Categories(PrivacyEntityModel entity)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var property in entity.Properties)
            if (property.Classification is { } classification)
                seen.Add(classification.Category);

        return [.. seen];
    }

    private IReadOnlyList<PrivacyEntityModel> Classified()
        => [.. _entities.Where(e => e.HasPersonalData)];
}
