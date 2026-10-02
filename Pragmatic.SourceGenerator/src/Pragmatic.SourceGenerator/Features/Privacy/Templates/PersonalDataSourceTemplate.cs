using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Transforms;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
///     Generates <c>{Entity}PersonalDataSource</c> — the <c>IPersonalDataSource</c> that finds one
///     entity's rows for a subject and hands them to the generated extractor.
/// </summary>
/// <remarks>
///     <para>
///         This is the half the extractor cannot be. <c>Extract</c> starts from an entity already in
///         hand; an access request starts from a subject reference. The bridge is a query, which is why
///         this type exists at all and why it is emitted only where EF Core is present.
///     </para>
///     <para>
///         The extractor stays a static class and is called from here rather than being turned into the
///         service itself. The projection has to keep working without a database — an export assembled
///         from an aggregate already in memory uses it too — and merging the two would take that away.
///     </para>
/// </remarks>
internal sealed class PersonalDataSourceTemplate : PrivacyAdapterTemplate
{
    private readonly string _typeName;
    private readonly string _extractorName;

    public PersonalDataSourceTemplate(
        PrivacyEntityModel model, SubjectRoute route, SubjectIdentifierMatch match)
        : base(model, route, match)
    {
        _typeName = NamingHelper.AppendSuffix(model.TypeName, "PersonalDataSource");
        _extractorName = NamingHelper.AppendSuffix(model.TypeName, "PersonalDataExtractor");
    }

    protected override string? TriggerInfo => $"[PersonalData] access adapter for {Model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_typeName, "PrivacySource", Model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("Microsoft.EntityFrameworkCore");

        if (!string.IsNullOrEmpty(Model.Namespace))
        {
            AppendNamespace(Model.Namespace);
            AppendLine();
        }

        XmlSummary(
            "SG-generated <see cref=\"global::Pragmatic.Privacy.IPersonalDataSource\"/> over " +
            $"<see cref=\"{Model.TypeName}\"/>, for subject access and portability requests.");

        Class(_typeName, RenderBody,
            interfaces: ["global::Pragmatic.Privacy.IPersonalDataSource"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        RenderDependencies(_typeName);

        XmlSummary(
            "Names this source in the subject's export. The full type name, because two sources " +
            "reporting the same category is an exception, not a merge.");
        AppendLine($"public string Category => \"{Model.FullTypeName}\";");
        AppendLine();

        XmlInheritDoc();
        AppendLine(
            "public async global::System.Threading.Tasks.ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> " +
            "CollectAsync(string subjectRef, global::System.Threading.CancellationToken ct = default)");
        Block(RenderCollect);
    }

    private void RenderCollect()
    {
        RenderIdentityLookup("[]");

        Comment("AsNoTracking: an access request only reads, and a tracked copy would be one more place " +
                "the subject's data lives for as long as the scope does.");
        // Past the soft-delete filter and no other, as the erasure step: a soft-deleted row is
        // still data held about the subject, and an access request answers for what is held.
        AppendLine($"var rows = await _db.Set<global::{Model.FullTypeName}>()");
        IncreaseIndent();
        AppendLine(".AsNoTracking()");
        AppendLine(".IgnoreQueryFilters(new[] { \"SoftDelete\" })");
        AppendLine($".Where({RowsForSubject()})");
        AppendLine(".ToListAsync(ct)");
        AppendLine(".ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine();

        AppendLine("var records = new List<IReadOnlyDictionary<string, object?>>(rows.Count);");
        AppendLine();
        AppendLine("foreach (var row in rows)");
        IncreaseIndent();
        AppendLine($"records.Add(global::{Sibling(_extractorName)}.Extract(row));");
        DecreaseIndent();
        AppendLine();
        AppendLine("return records;");
    }
}
