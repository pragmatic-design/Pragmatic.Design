using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Transforms;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
///     Generates <c>{Entity}ErasureStep</c> — the <c>IErasureStep</c> that finds one entity's rows for
///     a subject and applies the generated plan to them.
/// </summary>
/// <remarks>
///     <para>
///         The plan says <em>what</em> happens to each field and is a static class because it needs a
///         row. This says <em>which</em> rows and commits the result, which needs a database. Keeping
///         them apart is what lets the orchestrator own the order across steps — the part that decides
///         whether an interruption leaves something recoverable.
///     </para>
///     <para>
///         <b>Row deletion is decided here, at generation time.</b> The plan exposes it as a constant,
///         and branching on a constant at run time is unreachable code the compiler is right to
///         complain about.
///     </para>
/// </remarks>
internal sealed class ErasureStepTemplate : PrivacyAdapterTemplate
{
    /// <summary>
    ///     Where the generated steps sit on the orchestrator's ascending scale.
    /// </summary>
    /// <remarks>
    ///     Below it — 0 to 99 — is left free on purpose, and documented on <c>IErasureStep.Order</c>:
    ///     a step that reclaims stored files has to run before the rows pointing at them, or a crash
    ///     orphans the file with nothing able to find it again.
    /// </remarks>
    private const int SubjectOrder = 100;

    private readonly string _typeName;
    private readonly string _planName;
    private readonly bool _deletesRow;

    public ErasureStepTemplate(PrivacyEntityModel model, SubjectRoute route, SubjectIdentifierMatch match)
        : base(model, route, match)
    {
        _typeName = NamingHelper.AppendSuffix(model.TypeName, "ErasureStep");
        _planName = NamingHelper.AppendSuffix(model.TypeName, "ErasurePlan");
        _deletesRow = model.Properties.Any(p => p.Classification is { Erasure: "Delete" });
    }

    protected override string? TriggerInfo => $"[PersonalData] erasure adapter for {Model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_typeName, "PrivacyErasureStep", Model.Namespace), ToSourceText());

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
            "SG-generated <see cref=\"global::Pragmatic.Privacy.IErasureStep\"/> over " +
            $"<see cref=\"{Model.TypeName}\"/>, applying its erasure plan to the subject's rows.");

        Class(_typeName, RenderBody,
            interfaces: ["global::Pragmatic.Privacy.IErasureStep"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        RenderDependencies(_typeName);

        XmlSummary("Names this step in the erasure record.");
        AppendLine($"public string Name => \"{Model.FullTypeName}\";");
        AppendLine();

        XmlSummary(
            "Furthest from the subject first: those rows hold the foreign keys into the ones nearer it, " +
            "so clearing them in the other order can leave a reference to a row that is already gone.");
        AppendLine($"public int Order => {SubjectOrder - Route.Depth};");
        AppendLine();

        // Read off the plan rather than recomputed here, so the constant that states it and the step
        // that reports it cannot disagree. A constant read by nobody would let an erasure with no key
        // destroyer leave a DestroyKey field neither cleared nor unreadable while reporting success.
        XmlInheritDoc();
        AppendLine(
            $"public bool RequiresKeyDestruction => global::{Sibling(_planName)}.RequiresKeyDestruction;");
        AppendLine();

        XmlInheritDoc();
        AppendLine(
            "public async global::System.Threading.Tasks.ValueTask<global::Pragmatic.Privacy.ErasureStepResult> " +
            "EraseAsync(string subjectRef, global::System.Threading.CancellationToken ct = default)");
        Block(RenderErase);
    }

    private void RenderErase()
    {
        RenderIdentityLookup("global::Pragmatic.Privacy.ErasureStepResult.Nothing");

        Comment("Tracked on purpose: the plan writes to these instances and SaveChanges persists them.");
        // ⚠️ Past the soft-delete filter and no other. A soft-deleted row is still stored
        // personal data, and a subject is usually erased after they have been soft-deleted: read through
        // the raw set, the filter hid their row and every row reached through a navigation to it, and the
        // erasure reported itself total having erased none of them. Tenant stays on.
        AppendLine($"var rows = await _db.Set<global::{Model.FullTypeName}>()");
        IncreaseIndent();
        AppendLine(".IgnoreQueryFilters(new[] { \"SoftDelete\" })");
        AppendLine($".Where({RowsForSubject()})");
        AppendLine(".ToListAsync(ct)");
        AppendLine(".ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine();

        AppendLine("if (rows.Count == 0) return global::Pragmatic.Privacy.ErasureStepResult.Nothing;");
        AppendLine();

        if (_deletesRow)
        {
            Comment("A field classified ErasureStrategy.Delete makes the whole row personal data, so the " +
                    "row goes rather than being blanked.");
            AppendLine($"_db.Set<global::{Model.FullTypeName}>().RemoveRange(rows);");
        }
        else
        {
            AppendLine("foreach (var row in rows)");
            IncreaseIndent();
            AppendLine($"global::{Sibling(_planName)}.Apply(row, subjectRef);");
            DecreaseIndent();
        }

        AppendLine();

        if (_deletesRow)
        {
            // Erasure is the one caller that has to mean it. Soft delete is enforced at save time, so
            // without stepping out of it a subject's request would flag the rows, report the count it
            // erased, and leave the data in the table.
            AppendLine("using (global::Pragmatic.Persistence.Entity.SoftDeleteScope.Suspend())");
            Block(() => AppendLine("await _db.SaveChangesAsync(ct).ConfigureAwait(false);"));
        }
        else
        {
            AppendLine("await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        }

        AppendLine();

        RenderResult();
    }

    /// <summary>
    ///     Reports what was erased and what was kept, reading the retained list off the plan so the two
    ///     cannot disagree about the reason.
    /// </summary>
    private void RenderResult()
    {
        if (!Model.Properties.Any(p => p.Classification is { IsRetained: true }))
        {
            AppendLine("return global::Pragmatic.Privacy.ErasureStepResult.Erased(rows.Count);");
            return;
        }

        Comment("Retained fields are reported once per step, not once per row: the obligation is a " +
                "property of the field, and repeating it per row would drown the answer.");
        AppendLine("var retained = new List<global::Pragmatic.Privacy.RetainedItem>();");
        AppendLine();
        AppendLine($"foreach (var (field, reason, requiresKey) in global::{Sibling(_planName)}.Retained)");
        IncreaseIndent();
        AppendLine(
            $"retained.Add(new global::Pragmatic.Privacy.RetainedItem(\"{Model.TypeName}.\" + field, reason, requiresKey));");
        DecreaseIndent();
        AppendLine();
        AppendLine("return new global::Pragmatic.Privacy.ErasureStepResult(rows.Count, retained);");
    }
}
