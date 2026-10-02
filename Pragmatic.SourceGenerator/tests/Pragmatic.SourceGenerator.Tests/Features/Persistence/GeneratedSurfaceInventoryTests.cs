using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The inventory of the surface Persistence generates, snapshotted.
/// </summary>
/// <remarks>
///     <para>
///         The generated members are what a consumer writes against, and this test is what watches them:
///         without it a template that renamed <c>Invoice.Expr</c> or moved a filter out of its entity
///         would break every caller while the build stays green.
///     </para>
///     <para>
///         The corpus below is the input; the verified file beside this one is the answer. One entity
///         per feature, named after the feature, so a reader of the snapshot can tell what produced a
///         given artefact without leaving it.
///     </para>
/// </remarks>
public class GeneratedSurfaceInventoryTests
{
    /// <summary>
    ///     Every feature the corpus declares must appear in the output.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the assertion that makes the snapshot trustworthy. A corpus attribute whose
    ///         namespace is not imported — <c>[Projectable]</c>, <c>[ComputedFilter]</c> — does not bind:
    ///         the generator sees an undecorated property, and the inventory would record — accurately,
    ///         and falsely — that the feature emits no surface.
    ///     </para>
    ///     <para>
    ///         A corpus entry that stops triggering is invisible in a snapshot: the section simply is not
    ///         there, and a missing section reads like a feature that was never covered. So the expected
    ///         artefacts are named here, where their absence is a failure rather than a silence.
    ///     </para>
    /// </remarks>
    [Fact]
    public void EveryDeclaredFeatureProducesItsArtefact()
    {
        var result = Run();

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG9000").Should().BeEmpty(
            "PRAG9000 means a generator threw and its output was swallowed");

        var hints = result.GeneratedTrees
            .Select(t => t.FilePath)
            .Select(p => p[(p.LastIndexOfAny(['/', '\\']) + 1)..])
            .ToList();

        string[] expected =
        [
            // traits and keys
            "Plain.Traits.g.cs", "Auditable.Traits.g.cs", "SoftDeleted.Traits.g.cs",
            "SoftDeleted.SoftDeleteFilter.g.cs", "WithGeneratedValue.Number.ValueGenerator.g.cs",
            // ownership and visibility
            "Owned.Ownership.g.cs", "Owned.OwnershipFilter.g.cs",
            "Scoped.Scoping.g.cs", "Scoped.ScopedDataFilter.g.cs",
            "OwnedAndScoped.DataAccessFilter.g.cs",
            // relations, inheritance, trees
            "RelationParent.Relations.g.cs", "InheritanceBase.InheritanceMapping.g.cs",
            "TreeNode.HierarchyQuery.g.cs",
            // computed expressions
            "WithProjectable.Projectable.g.cs", "WithProjectable.ComputedFilter.g.cs",
            // behaviour
            "WithStateMachine.StateMachine.g.cs", "LookupTable.LookupCacheLoader.g.cs",
            // stored aggregate, attachments, temporal
            "RollUpParent.RollUp.g.cs", "_Infra.RollUp.Registration.g.cs",
            "Attachment.PolymorphicAttachment.g.cs", "Plain.AttachmentNavigation.g.cs",
            "TemporalRow.TemporalFilter.g.cs", "TemporalRow.TemporalValidation.g.cs",
            "TemporalRow.TemporalExtensions.g.cs", "TemporalRow.TimelineQuery.g.cs",
            // query side
            "SearchPlain.Query.g.cs", "GetOnePlain.Query.g.cs",
            "PlainFilter.FilterDto.g.cs", "PlainFilter.TypeConverter.g.cs",
            "PlainGridFilter.GridFilter.g.cs", "PlainGridAdapter.GridAdapter.g.cs",
            "RelationChildTotals.QueryView.g.cs",
            "PlainPatch.Patch.g.cs", "LoadManualNavParent.LoadingProfile.g.cs",
            "BridgedGridFilterBridge.GridBridge.g.cs",
            // trait combinations
            "AuditableSoftDeleted.Traits.g.cs", "AuditableSoftDeleted.SoftDeleteFilter.g.cs",
            "AuditedSoftDeleted.SoftDeleteFilter.g.cs", "EveryTrait.SoftDeleteFilter.g.cs",
            "CascadeParent.SoftDeleteFilter.g.cs", "CascadeChild.SoftDeleteFilter.g.cs",
            // the "I declare it myself" branch
            "ManualOwnerId.OwnershipFilter.g.cs", "ManualAuditableProps.Traits.g.cs",
            // tenant and state machines
            "TenantScoped.TenantFilter.g.cs",
            // state machine: events and a renamed property
            "WithEventfulStateMachine.StateMachine.g.cs", "NamedStateProperty.StateMachine.g.cs",
            // untyped temporal
            "UntypedTemporalRow.TemporalValidation.g.cs",
            // query variants
            "UnpagedPlain.Query.g.cs", "EntityResultQuery.Query.g.cs", "RecordQuery.Query.g.cs",
            "JoinedQuery.Query.g.cs", "GroupedQuery.Query.g.cs", "SearchAcrossQuery.Query.g.cs",
            "PublishedQuery.Query.g.cs", "_ReadContract.PlainReadContract.g.cs",
            // the result DTO: mapping, projection, includes derived from its shape
            "PlainDto.Mapping.g.cs", "ManualNavParent.Includes.g.cs", "Plain.Projections.g.cs",
            // value object, cascade, inheritance x trait
            "Money.ValueObject.g.cs", "CascadeTargetEntity.SourceName.CascadeHandler.g.cs",
            "TraitedBase.InheritanceMapping.g.cs", "TraitedBase.SoftDeleteFilter.g.cs",
            // [Resource]: the whole CRUD scaffold from one line
            "_Resource.Widget.Create.Endpoint.g.cs", "_Resource.Widget.Read.Endpoint.g.cs",
            "_Resource.Widget.Update.Endpoint.g.cs", "_Resource.Widget.Delete.Endpoint.g.cs",
            "_Resource.Widget.List.Endpoint.g.cs", "_Resource.Widget.Search.Endpoint.g.cs",
            "ResourceCreateWidgetMutation.MutationInvoker.g.cs", "WidgetReadDto.Mapping.g.cs",
            // per-entity staples and host side
            "Plain.Repository.g.cs", "Plain.Specs.g.cs", "Plain.Setters.g.cs", "Plain.Create.g.cs",
            "EntityConfig.Inventory.Plain.g.cs", "DbContext.Inventory.g.cs",
            "_Infra.Persistence.RepositoryRegistration.g.cs", "_Infra.Persistence.QueryFilters.g.cs",
        ];

        // Three corpus declarations are deliberately NOT listed here, because they produce no per-type
        // artefact: [HasPresets]/[PresetProvider], [Invariant] and [ComputedDefault] are read at runtime
        // by the mutation pipeline, not from a generated file. They stay in the corpus because their
        // effect shows elsewhere (in Create, in the invoker), and they are named here so that their
        // absence from the list is a written choice rather than an oversight.
        var missing = expected.Where(e => !hints.Any(h => h.EndsWith(e, System.StringComparison.Ordinal))).ToList();

        missing.Should().BeEmpty(
            "a corpus declaration that produces nothing leaves a hole the snapshot cannot show");
    }

    /// <summary>
    ///     <c>[LoadWith&lt;T&gt;]</c> includes the navigations declared with <c>[Relation.*]</c>, not just
    ///     the ones written in source.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>LoadingProfileTransform</c> reads navigations from the <c>[Relation.*]</c> attributes, not
    ///         off the entity symbol alone. A navigation that exists only because <c>[Relation.*]</c>
    ///         describes it is emitted by this same generator pass, so the symbol does not carry it yet —
    ///         a generator cannot see another generator's output.
    ///     </para>
    ///     <para>
    ///         The failure this guards against is not a profile that goes missing — that gets noticed. It
    ///         is a profile generated <b>incomplete</b>: <c>MaxDepth</c> promises "every navigation up to
    ///         N" and delivers whichever subset the author happened to write by hand. The reference
    ///         application would hide it by coincidence: <c>Reservation</c> writes <c>Guest</c>,
    ///         <c>Property</c> and <c>RoomType</c> in source, so those three are included either way, and
    ///         only its <c>[Relation.OneToMany&lt;RoomAssignment&gt;]</c> tells the two apart.
    ///     </para>
    ///     <para>
    ///         ⚠️ The assertion names a navigation the pluraliser actually produces. A name it never
    ///         produces — <c>ManualNavChildren</c>, say — passes vacuously and reports success whatever
    ///         the transform does.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     The corpus plus everything generated from it compiles, with no errors.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this the snapshot measures the wrong thing. The inventory reads the generated
    ///         trees through Roslyn and never asks the compilation whether they are valid, so generated
    ///         code that does not compile — a name that does not resolve, an accessibility the consumer
    ///         cannot reach, a duplicate member — would be recorded in the snapshot and approved as the
    ///         surface. Green, accurate, and describing something nobody can build.
    ///     </para>
    ///     <para>
    ///         It also holds the corpus to what a real consumer must write: an entity has to declare
    ///         <c>IEntity</c> itself, because the generated repository implements
    ///         <c>IRepository&lt;TEntity&gt;</c> and that constraint is <c>where TEntity : class,
    ///         IEntity</c>. The generator supplies the members and never the base list.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheCorpusAndEverythingGeneratedFromItCompiles()
    {
        var result = Run();

        // Grouped by message: one bad name in a generated file produces an error per use, and a
        // hundred copies of the same line push the ones that differ past the end of the message.
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .GroupBy(d => $"{d.Id} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}",
                System.StringComparer.Ordinal)
            .Select(g => $"{g.Key} ×{g.Count()} "
                + $"[{System.IO.Path.GetFileName(g.First().Location.SourceTree?.FilePath ?? "<corpus>")}]")
            .OrderBy(m => m, System.StringComparer.Ordinal)
            .ToList();

        // Joined rather than asserted as a collection: a collection assertion truncates after a handful
        // of entries, and the entry that explains the rest is rarely in the first handful.
        Assert.True(
            errors.Count == 0,
            "The corpus, or something generated from it, does not compile. The snapshot describes a "
            + "surface a consumer compiles against, so this fails here rather than being approved into "
            + "the verified file.\n\n" + string.Join("\n", errors));

        // And no diagnostic of ours fires on shapes that are correct. A generator error is not a
        // compilation error — it never reaches GetCompilationErrors — so without this a diagnostic
        // that over-fires passes here and breaks the first real build instead. PRAG0704 did exactly
        // that: it read an attribute off a [Resource] DTO this compilation was still generating.
        // One shape in the corpus is declared to be reported: ContradictoryPart carries [PartOf<T>] and
        // [Resource] together, and PRAG2611 is what says so. Named here rather than filtered out by
        // severity, so it stays an expectation and not a hole.
        string[] reportedOnPurpose = ["PRAG2611"];

        var ourErrors = GeneratorTestHelper.GetGeneratorDiagnostics(result)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !reportedOnPurpose.Contains(d.Id, System.StringComparer.Ordinal))
            .Select(d => $"{d.Id} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .Distinct(System.StringComparer.Ordinal)
            .OrderBy(m => m, System.StringComparer.Ordinal)
            .ToList();

        Assert.True(
            ourErrors.Count == 0,
            "Apart from the one it declares on purpose, the corpus uses only shapes the generator "
            + "supports — so a PRAG error here means the diagnostic fires on something correct.\n\n" + string.Join("\n", ourErrors));
    }

    [Fact]
    public void LoadWithIncludesNavigationsThatComeFromRelationAttributes()
    {
        var trees = Run().GeneratedTrees.ToList();

        var profile = trees.SingleOrDefault(
            t => t.FilePath.EndsWith("LoadManualNavParent.LoadingProfile.g.cs", System.StringComparison.Ordinal));

        profile.Should().NotBeNull("the entity has navigations, so a profile is produced");

        var text = profile!.ToString();

        text.Should().Contain("Include(e => e.Side)",
            "Side is written in source, and was the only kind the transform used to see");

        text.Should().Contain("Include(e => e.ManualNavChilds)",
            "the collection exists only because [Relation.OneToMany<ManualNavChild>] describes it — "
            + "this is the include that was silently missing");

        text.Should().NotContain("ThenInclude(e => e.ManualNavParent)",
            "the child's only declared relation is the way back to the parent, and EF Core refuses an "
            + "include that walks back up the include tree — the fix-up populates it anyway. That depth "
            + "2 does reach a child's own FORWARD relation is covered by "
            + "LoadWith_SkipsTheNavigationThatWalksBackUpTheIncludeTree, whose corpus has one");
    }

    /// <summary>
    ///     A relation that crosses a boundary produces a foreign key and no navigation, so the loading
    ///     profile must not try to include it.
    /// </summary>
    /// <remarks>
    ///     The other half of reading relations off the attributes: the attribute is there, the
    ///     navigation is not. Including it would emit <c>Include(e =&gt; e.Faraway)</c> against a property
    ///     that was never generated — a CS1061 inside a file its author cannot edit. The rule lives in
    ///     <c>RelationGraphBuilder.IsCrossBoundary</c> and is read a second time in
    ///     <c>LoadingProfileTransform</c>; this test is what keeps the two honest.
    /// </remarks>
    [Fact]
    public void LoadWithSkipsRelationsThatCrossABoundary()
    {
        var trees = Run().GeneratedTrees.ToList();

        var profile = trees.SingleOrDefault(
            t => t.FilePath.EndsWith("LoadAcrossBoundary.LoadingProfile.g.cs", System.StringComparison.Ordinal));

        profile.Should().NotBeNull("the entity has a same-boundary navigation too, so a profile is produced");

        var text = profile!.ToString();

        text.Should().Contain("Include(e => e.Plain)",
            "the same-boundary relation does generate a navigation");

        text.Should().NotContain("Faraway",
            "the relation crosses a boundary, so no navigation was generated and there is nothing to include");
    }

    /// <summary>
    ///     <c>[PartOf&lt;TParent&gt;]</c> together with <c>[Resource]</c> is reported — PRAG2611.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[PartOf]</c> means "written through the parent, in the parent's transaction, behind the
    ///         parent's permissions and validation". <c>[Resource]</c> means "addressable on its own, with
    ///         create, update and delete endpoints". Both on one type is a contradiction, and both
    ///         <c>02-entity-system.md</c> and <c>PartOfAttribute</c>'s own XML already claimed it "is
    ///         reported as one" — the only thing missing was the report.
    ///     </para>
    ///     <para>
    ///         Until PRAG2611 landed the second declaration simply won: the generator scaffolded the full
    ///         CRUD in silence, so a child aggregate could be created and deleted over HTTP without ever
    ///         passing through its parent. This test was written the other way round first — pinning the
    ///         hole as generated surface — and inverted when the diagnostic arrived.
    ///     </para>
    ///     <para>
    ///         The scaffold is still emitted alongside the error: an Error fails the build, so nothing
    ///         reaches a consumer, and stopping generation as well would replace one clear message with a
    ///         cascade of CS errors about types that were never written.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ResourceOnAPartOfEntityIsReported()
    {
        var result = Run();

        var diagnostic = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2611").ToList();

        diagnostic.Should().NotBeEmpty(
            "an entity that is written through its parent cannot also be addressable on its own");

        diagnostic[0].Severity.Should().Be(DiagnosticSeverity.Error,
            "a warning would let the contradiction ship; the endpoints it generates bypass the parent's "
            + "permissions, validation and events");

        diagnostic[0].GetMessage().Should().Contain("ContradictoryPart")
            .And.Contain("RelationParent",
                "the message has to name both halves of the contradiction to be actionable");
    }

    [Fact]
    public Task PersistenceSurface()
    {
        var result = Run();

        var inventory = GeneratedSurfaceInventory.Render(
            result.GeneratedTrees, "Generated surface — Pragmatic.Persistence");

        return Verify(inventory);
    }

    /// <summary>
    ///     The reference set. Every entry is here because something generated names a type from it, and
    ///     <see cref="GeneratorTestHelper.FromType{T}" /> adds one assembly and not its dependencies —
    ///     so an assembly missing here does not fail loudly, it makes the feature that needs it either
    ///     not bind at all or generate code that cannot compile.
    /// </summary>
    private static SourceGenRunResult Run()
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Corpus, References());

    /// <summary>
    ///     Everything the corpus and its generated output are compiled against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="GeneratorTestHelper.FromType{T}" /> adds one assembly and not its dependencies,
    ///         and a missing one does not fail loudly: the attribute does not bind and the feature is
    ///         silently not generated, or the generated file names a type nothing resolves. Both read as
    ///         a clean run.
    ///     </para>
    ///     <para>
    ///         So the set is explicit, and a name that cannot be resolved throws rather than being
    ///         skipped — a dropped reference would put the hole straight back.
    ///     </para>
    /// </remarks>
    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        // ITenantEntity lives in Pragmatic.Abstractions: without this the interface does not bind and
        // the tenant filter is silently not generated.
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        // The assembly that defines TypeConverterAttribute, not the facade that only forwards
        // the TypeConverter class: with the class visible and the attribute not, the generated
        // [TypeConverter(...)] resolves to a non-attribute type (CS0616).
        GeneratorTestHelper.FromType<global::System.ComponentModel.TypeConverterAttribute>(),
        .. ByName(
            // the runtime the generated code stands on
            "System.Text.Json",
            "System.Linq.Queryable",
            "System.ComponentModel.Annotations",
            "System.Diagnostics.DiagnosticSource",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.AspNetCore.Http.Abstractions",
            "Microsoft.AspNetCore.Http.Results",
            "Microsoft.AspNetCore.Routing",
            "Microsoft.AspNetCore.Routing.Abstractions",
            "Microsoft.AspNetCore.RateLimiting",
            "Microsoft.AspNetCore.Metadata",
            "Microsoft.Extensions.Configuration.Abstractions",
            "Microsoft.AspNetCore.Authorization",
            "Microsoft.AspNetCore.Authorization.Policy",
            "Microsoft.AspNetCore.ResponseCompression",
            "Microsoft.AspNetCore.Hosting.Abstractions",
            "Microsoft.AspNetCore.Http.Extensions",
            "System.Threading.RateLimiting",
            "System.Private.Uri",
            // the framework the generated code calls into
            "System.Security.Claims",
            "System.ComponentModel.TypeConverter",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            // The repository names EfMutationHelpers to link rows by key. A module assembly gets
            // this transitively through Pragmatic.Persistence.EFCore; this set is hand-built, so
            // it has to say so — otherwise the corpus reports CS0234 on a reference a real
            // consumer has.
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Caching",
            "Pragmatic.Events",
            "Pragmatic.Audit",
            "Pragmatic.Audit.EFCore",
            "Pragmatic.Authorization",
            "Pragmatic.Actions",
            "Pragmatic.Endpoints",
            "Pragmatic.Endpoints.AspNetCore"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));

    /// <summary>
    ///     One entity per feature, named after the feature, so the artefact name says which declaration
    ///     produced it and a reader of the snapshot never has to come back here to find out.
    /// </summary>
    private const string Corpus = """
        // What the SDK gives every consumer through ImplicitUsings, declared here because a bare Roslyn
        // compilation has none — and a generated file is its own tree, so only a global using reaches it.
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;

        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Persistence.Query.Filters;
        using Pragmatic.Persistence.StateMachine;

        namespace Inventory;

        public sealed class InventoryBoundary;

        // ── traits ────────────────────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class Plain : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Auditable]
        public partial class Auditable : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [SoftDelete]
        public partial class SoftDeleted : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Audited]
        public partial class Audited : IEntity
        {
            /// <summary>Renamed: the schema must emit a RENAME, not a DROP + ADD.</summary>
            [RenamedFrom("Label")]
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [ConcurrencyAware]
        public partial class ConcurrencyAware : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── keys ──────────────────────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithLogicKey : IEntity
        {
            [LogicKey]
            public string Code { get; private set; } = "";
        }

        /// <summary>Two parts: the lookup takes both, and is named after both.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithCompositeLogicKey : IEntity
        {
            [LogicKey]
            public string Code { get; private set; } = "";

            [LogicKey]
            public int Season { get; private set; }
        }

        /// <summary>
        ///     Declaration order and <c>Order</c> disagree on purpose: the index and the lookup have to
        ///     follow <c>Order</c>, and both are in the snapshot below to say which one won.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithOrderedLogicKey : IEntity
        {
            [LogicKey(Order = 2)]
            public string CountryCode { get; private set; } = "";

            [LogicKey(Order = 1)]
            public string Vat { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithGeneratedValue : IEntity
        {
            [GeneratedValue("INV-{YYYY}{MM}-{SEQ:5}")]
            public string Number { get; private set; } = "";
        }

        // ── ownership and visibility ──────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [HasOwner]
        public partial class Owned : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [HasAccessScopes]
        public partial class Scoped : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>Both together collapse into one filter, OR-composed.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [HasOwner]
        [HasAccessScopes]
        public partial class OwnedAndScoped : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── relations ─────────────────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.OneToMany<RelationChild>]
        [Relation.OneToOne<RelationSide>]
        public partial class RelationParent : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<RelationParent>]
        public partial class RelationChild : IEntity
        {
            public decimal Amount { get; private set; }
        }

        /// <summary>A DTO that flattens a navigation: it reads the parent's name.</summary>
        [Pragmatic.Mapping.Attributes.MapFrom<RelationChild>]
        public partial class RelationChildWithParentDto
        {
            [Pragmatic.Mapping.Attributes.MapProperty("RelationParent.Name")]
            public string ParentName { get; init; } = "";
        }

        /// <summary>
        ///     ⚠️ The control next to PRAG0533: an <b>update</b> can answer through a navigation,
        ///     because the generated query carries the <c>Include</c>. Without it, a diagnostic firing on
        ///     every navigated DTO would still pass the create case.
        /// </summary>
        [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
        [ReturnsDto<RelationChildWithParentDto>]
        [Pragmatic.Endpoints.Attributes.Endpoint(
            Pragmatic.Endpoints.HttpVerb.Put, "api/relation-children/{id}/amount")]
        public partial class RetouchRelationChildMutation
            : Pragmatic.Actions.Mutation.Mutation<RelationChild>
        {
            public required Guid Id { get; init; }
            public required decimal Amount { get; init; }
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class RelationSide : IEntity
        {
            public string Note { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToMany<Tag, TaggedThingTag>]
        public partial class TaggedThing : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class Tag : IEntity
        {
            public string Label { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<TaggedThing>]
        [Relation.ManyToOne<Tag>]
        public partial class TaggedThingTag : IEntity
        {
            public bool IsPrimary { get; private set; }
        }

        /// <summary>
        ///     A child written through its parent, never addressed on its own. The ownership leans on a
        ///     declared edge, and with no collection on the parent the edge itself says it cascades.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<RelationParent>.WithNavigation("Parent", OnDelete = DeleteBehavior.Cascade)]
        [PartOf<RelationParent>]
        public partial class AggregatePart : IEntity
        {
            public string Description { get; private set; } = "";
        }

        // ── inheritance and trees ─────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "Kind")]
        public abstract partial class InheritanceBase : IEntity
        {
            public decimal Amount { get; private set; }
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class InheritanceDerived : InheritanceBase
        {
            public string Reason { get; private set; } = "";
        }

        /// <summary>
        ///     A tree: the edge is a relation declared to itself, and the hierarchy reads it.
        ///     ⚠️ <c>ParentId</c> is not written by hand: the hierarchy reads the relation, not the members.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<TreeNode>.WithNavigation("Parent", Required = false)]
        [GenerateHierarchy]
        public partial class TreeNode : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── computed expressions ──────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithProjectable : IEntity
        {
            public decimal Subtotal { get; private set; }
            public decimal TaxRate { get; private set; }

            [Projectable]
            public decimal TotalWithTax => Subtotal * (1 + TaxRate);

            [ComputedFilter]
            public bool IsHighValue => Subtotal > 10_000m;
        }

        // ── state machine ─────────────────────────────────────────────────────────────────────────

        public enum FlowState
        {
            [InitialState]
            Draft,

            [TransitionFrom(FlowState.Draft)]
            Submitted,

            [TransitionFrom(FlowState.Submitted)]
            Approved,

            [TransitionFrom(FlowState.Draft)]
            [TransitionFrom(FlowState.Submitted)]
            Cancelled
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [StateMachine<FlowState>]
        public partial class WithStateMachine : IEntity
        {
            public FlowState Status { get; private set; }
        }

        // ── lookup ────────────────────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Lookup]
        public partial class LookupTable : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── stored aggregate over children ────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.OneToMany<RollUpChild>]
        public partial class RollUpParent : IEntity
        {
            [RollUp<RollUpChild>(nameof(RollUpChild.Amount))]
            public decimal Subtotal { get; private set; }
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<RollUpParent>]
        public partial class RollUpChild : IEntity
        {
            public decimal Amount { get; private set; }
        }

        // ── polymorphic attachments ───────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [PolymorphicAttachment]
        [Attachable<Plain>]
        [Attachable<Auditable>]
        public partial class Attachment : IEntity
        {
            public string FileName { get; private set; } = "";
        }

        // ── temporal ──────────────────────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<Plain>]
        [TemporalRelation<Plain>(MaxActive = 1, AllowOverlap = false)]
        [GenerateTimeline]
        public partial class TemporalRow : IEntity
        {
            public decimal Rate { get; private set; }
            public DateTimeOffset ValidFrom { get; private set; }
            public DateTimeOffset? ValidTo { get; private set; }
        }

        // ── invariants and defaults ───────────────────────────────────────────────────────────────

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithInvariant : IEntity
        {
            [DefaultValue(true)]
            public bool IsActive { get; private set; }

            public int Quantity { get; private set; }

            [Invariant("Quantity must stay positive")]
            public bool QuantityIsPositive() => Quantity > 0;
        }

        // ── queries ───────────────────────────────────────────────────────────────────────────────

        [Query<Plain, PlainDto>]
        public partial class SearchPlain
        {
            [Filter(Operator = FilterOperator.Contains)]
            public string? Name { get; init; }

            [Filter]
            public Guid? Id { get; init; }

            [Sort(MapTo = "Name", DefaultDirection = SortDirection.Descending)]
            public SortDirection? NameSort { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        /// <summary>One row, and a 404 when there is none.</summary>
        [Query<Plain, PlainDto>(Single = true)]
        public partial class GetOnePlain
        {
            [Filter(MapTo = "PersistenceId")]
            public Guid Id { get; init; }
        }

        // A query's result type needs BOTH attributes. [MapFrom<T>] gives FromEntity and Selector;
        // the Projection the generated Apply names comes from [GenerateProjection], and without it the
        // query does not compile.
        [MapFrom<Plain>]
        [GenerateProjection]
        public sealed partial class PlainDto
        {
            public Guid Id { get; init; }
            public string Name { get; init; } = "";
        }

        [MapFrom<RelationChild>]
        [GenerateProjection]
        public sealed partial class RelationChildDto
        {
            public Guid Id { get; init; }
            public decimal Amount { get; init; }
        }

        [MapFrom<ReachesAcrossBoundary>]
        [GenerateProjection]
        public sealed partial class ReachesAcrossBoundaryDto
        {
            public Guid Id { get; init; }
            public string Name { get; init; } = "";
        }

        [FilterDto<Plain>]
        public partial class PlainFilter
        {
            [Filter(Operator = FilterOperator.Contains)]
            public string? Name { get; init; }

            // ⚠️ [FilterGroup] is read by FilterDtoTransform only — that is, on a property OF a filter
            // DTO, whose type is another filter DTO. On a [Query] property it is ignored and the
            // property name is used as an entity column, which does not compile.
            [FilterGroup(FilterLogic.Or)]
            public PlainNameGroup? AnyName { get; init; }
        }

        [FilterDto<Plain>]
        public partial class PlainNameGroup
        {
            [Filter(Operator = FilterOperator.StartsWith, MapTo = "Name")]
            public string? Prefix { get; init; }

            [Filter(Operator = FilterOperator.EndsWith, MapTo = "Name")]
            public string? Suffix { get; init; }
        }

        [GridFilter<Plain>]
        public partial class PlainGridFilter
        {
            [Filterable(Operators = FilterOps.String)]
            public string? Name { get; set; }
            public StringOperator? NameOperator { get; set; }

            [Sort(MapTo = "Name")]
            public SortDirection? NameSort { get; set; }

            public int Page { get; set; } = 1;
            public int PageSize { get; set; } = 20;
        }

        [GridAdapter<Plain>(Framework = GridFramework.Both)]
        [GridField("name", Property = "Name")]
        [GridExclude("PersistenceId")]
        public partial class PlainGridAdapter;

        /// <summary>
        ///     The compile-time bridge from a canonical grid request to typed LINQ.
        /// </summary>
        /// <remarks>
        ///     <c>[Filterable]</c> on the two properties and not on <c>Secret</c>: the bridge is an
        ///     allowlist, so an undeclared property is not nameable by the client. The third property is
        ///     here so the snapshot records the exclusion rather than just the inclusions.
        /// </remarks>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [GenerateGridBridge]
        public partial class Bridged : IEntity
        {
            [Filterable]
            public string Name { get; private set; } = "";

            [Filterable]
            public decimal Amount { get; private set; }

            public string Secret { get; private set; } = "";
        }

        /// <summary>Tracked partial update: only the properties the caller marked as set.</summary>
        [Pragmatic.Persistence.Patch.Patch<Plain>]
        public partial class PlainPatch
        {
            public string? Name { get; set; }
        }

        /// <summary>
        ///     A loading profile over an entity whose navigation is written by hand.
        /// </summary>
        /// <remarks>
        ///     Deliberately NOT over an entity whose navigations come from [Relation.*]: the transform
        ///     reads them off the entity symbol, and a navigation the generator is emitting in this same
        ///     pass is not on the symbol yet. See LoadWithSeesOnlyHandWrittenNavigations.
        /// </remarks>
        [LoadWith<ManualNavParent>(MaxDepth = 2, SplitQuery = true)]
        [Query<ManualNavParent, ManualNavParentDto>]
        public partial class LoadManualNavParent
        {
            [Filter]
            public Guid? Id { get; init; }
        }

        /// <summary>
        ///     The shape the reference application has: one navigation written in source, one that only
        ///     [Relation.*] describes. The loading profile picks up the first and misses the second.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.OneToMany<ManualNavChild>]
        [Relation.OneToOne<RelationSide>.WithNavigation("Side")]
        public partial class ManualNavParent : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<ManualNavParent>]
        public partial class ManualNavChild : IEntity
        {
            public decimal Amount { get; private set; }
        }

        [MapFrom<ManualNavParent>]
        [GenerateProjection]
        public sealed partial class ManualNavParentDto
        {
            public Guid Id { get; init; }
            public string Name { get; init; } = "";
        }

        [QueryView<RelationChild>]
        [GroupBy<RelationChild>(Properties = "RelationParentId")]
        public partial class RelationChildTotals
        {
            [From<RelationChild>(Property = "RelationParentId")]
            public Guid ParentId { get; set; }

            [Sum<RelationChild>(Expression = "Amount")]
            public decimal Total { get; set; }

            [Count<RelationChild>]
            public int Lines { get; set; }

            [Avg<RelationChild>(Expression = "Amount")]
            public decimal Average { get; set; }

            [Min<RelationChild>(Expression = "Amount")]
            public decimal Smallest { get; set; }

            [Max<RelationChild>(Expression = "Amount")]
            public decimal Largest { get; set; }
        }

        // ── hand-written mutation ─────────────────────────────────────────────────────────────────

        /// <summary>
        ///     The only non-scaffolded mutation of the corpus: it carries [ReturnsDto], [WithoutFilter]
        ///     and [FilterMode], which live on the operation and not on the entity.
        /// </summary>
        [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
        [ReturnsDto<PlainDto>]
        [WithoutFilter<SoftDeleted>]
        [Pragmatic.Persistence.Query.Filters.FilterMode(
            Pragmatic.Persistence.Query.Filters.FilterMode.Admin)]
        public partial class RenamePlainMutation : Pragmatic.Actions.Mutation.Mutation<Plain>
        {
            public required Guid Id { get; init; }
            public string? Name { get; init; }
        }

        // ── loading strategy declared on the operation ────────────────────────────────────────────

        /// <summary>Raw: no filter, no tracking — the way for an export or an admin panel.</summary>
        [QueryStrategy(Strategy = QueryStrategy.Raw)]
        [Query<Plain, PlainDto>]
        public partial class RawStrategyQuery
        {
            [Filter]
            public Guid? Id { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        // ── combinazioni ──────────────────────────────────────────────────────────────────────────
        //
        // Not every possible combination — that would be 2^n — but every axis the templates really
        // branch on, in both states, and the pairs the templates combine. The axes are derived from
        // the `_model.Is*`/`_model.Has*` flags read in the Persistence templates.

        /// <summary>Traits + soft delete: Remove() becomes a flag, and the filter is added.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Auditable]
        [SoftDelete]
        public partial class AuditableSoftDeleted : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>Soft delete + audited: the bulk delete also writes the audit row.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Audited]
        [SoftDelete]
        public partial class AuditedSoftDeleted : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>All three: SaveChangesAsync changes return type AND Remove changes meaning.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Auditable]
        [Audited]
        [ConcurrencyAware]
        [SoftDelete]
        public partial class EveryTrait : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>Cascade: deleting the parent marks the children too.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [SoftDelete(Cascade = true)]
        [Relation.OneToMany<CascadeChild>]
        public partial class CascadeParent : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [SoftDelete]
        [Relation.ManyToOne<CascadeParent>]
        public partial class CascadeChild : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── the "I declare it myself" branch ──────────────────────────────────────────────────────

        /// <summary>PersistenceId written by hand: the generator steps back on that member.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class ManualPersistenceId : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; private set; } = "";
        }

        /// <summary>OwnerId written by hand: PRAG1104, the property is not generated but the filter is.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [HasOwner]
        public partial class ManualOwnerId : IEntity
        {
            public string OwnerId { get; private set; } = "";
            public string Name { get; private set; } = "";
        }

        /// <summary>All four trait properties declared: the group passes to the author.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Auditable]
        public partial class ManualAuditableProps : IEntity
        {
            public DateTimeOffset CreatedAt { get; set; }
            public string? CreatedBy { get; set; }
            public DateTimeOffset? UpdatedAt { get; set; }
            public string? UpdatedBy { get; set; }
            public string Name { get; private set; } = "";
        }

        // ── tenant ────────────────────────────────────────────────────────────────────────────────

        /// <summary>ITenantEntity declared: the tenant filter joins the chain.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class TenantScoped : IEntity, Pragmatic.MultiTenancy.ITenantEntity
        {
            public string TenantId { get; set; } = "";
            public string Name { get; private set; } = "";
        }

        // ── key types ──
        //
        // None: [Entity] carries no key type, every entity is identified by a Guid v7.

        // ── state machine with events ─────────────────────────────────────────────────────────────

        public sealed record FlowApproved(Guid Id);

        public enum EventfulState
        {
            [InitialState]
            Open,

            [TransitionFrom(EventfulState.Open)]
            [RaisesEvent<FlowApproved>]
            Approved
        }

        /// <summary>With [RaisesEvent]: the transition raises a domain event.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [StateMachine<EventfulState>]
        public partial class WithEventfulStateMachine : IEntity
        {
            public EventfulState Status { get; private set; }
        }

        /// <summary>Property = a name other than "Status", the default that stays silent when wrong.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [StateMachine<FlowState>(Property = "Phase")]
        public partial class NamedStateProperty : IEntity
        {
            public FlowState Phase { get; private set; }
        }

        // ── untyped temporal ──────────────────────────────────────────────────────────────────────

        /// <summary>Without TParent: AutoClosePrevious loses the scope parameter.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [TemporalRelation(MaxActive = 1)]
        public partial class UntypedTemporalRow : IEntity
        {
            public decimal Rate { get; private set; }
            public DateTimeOffset ValidFrom { get; private set; }
            public DateTimeOffset? ValidTo { get; private set; }
        }

        // ── query variants ────────────────────────────────────────────────────────────────────────

        /// <summary>Without Page/PageSize: IQuery instead of IPagedQuery.</summary>
        [Query<Plain, PlainDto>]
        public partial class UnpagedPlain
        {
            [Filter]
            public Guid? Id { get; init; }
        }

        /// <summary>A single type argument: the result is the entity, no projection.</summary>
        [Query<Plain>]
        public partial class EntityResultQuery
        {
            [Filter]
            public Guid? Id { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        /// <summary>Query declared as a record: the template branches on IsRecord.</summary>
        [Query<Plain, PlainDto>]
        public partial record RecordQuery
        {
            [Filter]
            public Guid? Id { get; init; }
        }

        /// <summary>Declared join + a filter on a path that crosses it.</summary>
        [Query<RelationChild, RelationChildDto>]
        [Join<RelationParent>(Via = "RelationParent")]
        public partial class JoinedQuery
        {
            [Filter(MapTo = "RelationParent.Name", Operator = FilterOperator.Contains)]
            public string? ParentName { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        /// <summary>Gruppo OR + filtro complesso JSON: due rami distinti del template.</summary>
        [Query<Plain, PlainDto>]
        public partial class GroupedQuery
        {
            [ComplexFilter]
            public PlainFilter? Complex { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        /// <summary>Read contract declared for another boundary.</summary>
        [Query<Plain, PlainDto>]
        [Published(ContractName = "PlainReadContract")]
        public partial class PublishedQuery
        {
            [Filter]
            public Guid? Id { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        /// <summary>Search across several columns from a single field.</summary>
        [Query<Plain, PlainDto>]
        public partial class SearchAcrossQuery
        {
            // ⚠️ [SearchAcross] is read by GridFilterTransform only: on a [Query] property it is
            // ignored, and the property name is used as the entity column — hence the MapTo, without
            // which this generates e.Search and does not compile. The working shape is on the
            // [GridFilter<Plain>] below.
            [SearchAcross("Name")]
            [Filter(Operator = FilterOperator.Contains, MapTo = "Name")]
            public string? Search { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        // ── relations: the shapes the diagnostics themselves name ─────────────────────────────────
        //
        // Not the cartesian product: the situations for which the framework has a diagnostic or a
        // dedicated branch. If it is worth warning the user about, it is worth a snapshot.

        /// <summary>Two relations to the SAME type: without WithNavigation they collide (PRAG0612).</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<Person>.WithNavigation("CreatedBy", Inverse = "Created")]
        [Relation.ManyToOne<Person>.WithNavigation("AssignedTo", Inverse = "Assigned")]
        public partial class TwoWaysToTheSameType : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>
        ///     The other end of the two relations above, declared rather than written by hand as
        ///     collections for <c>Inverse</c> on the child to bind to. Two relations to one type, so each
        ///     end names the other — that is what pairs them.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.OneToMany<TwoWaysToTheSameType>.WithNavigation("Created", Inverse = "CreatedBy")]
        [Relation.OneToMany<TwoWaysToTheSameType>.WithNavigation("Assigned", Inverse = "AssignedTo")]
        public partial class Person : IEntity
        {
            public string FullName { get; private set; } = "";
        }

        /// <summary>Foreign key with a chosen name, and an optional relation.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<Person>.WithNavigation("Author", ForeignKey = "WrittenById", Required = false)]
        public partial class OptionalReference : IEntity
        {
            public string Text { get; private set; } = "";
        }

        /// <summary>
        ///     REQUIRED navigation to a soft-delete entity: EF makes an INNER JOIN, and deleting the
        ///     target hides this row too. It is PRAG0705, the subtlest case of the model.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<SoftDeleted>.WithNavigation("Target", Required = true)]
        public partial class RequiredIntoSoftDeleted : IEntity
        {
            public string Note { get; private set; } = "";
        }

        /// <summary>
        ///     One-to-one declared from the dependent side, the one that carries the foreign key.
        /// </summary>
        /// <remarks>
        ///     ⚠️ <c>IsPrincipal = true</c> on this class, the one called <c>DependentSide</c>, would give
        ///     a navigation with <b>no</b> foreign key at all, and <c>PrincipalSide</c> with nothing. It
        ///     would compile, so the corpus would stay green; <c>PRAG0618</c> reports it.
        /// </remarks>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.OneToOne<PrincipalSide>.WithNavigation("Principal")]
        public partial class DependentSide : IEntity
        {
            public string Note { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class PrincipalSide : IEntity
        {
            public string Note { get; private set; } = "";
        }

        /// <summary>
        ///     Self-relation declared from both ends: the tree that points to itself. The children's
        ///     collection is declared by the end that receives it, not written by hand.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<SelfReferencing>.WithNavigation("Parent", Inverse = "Children", Required = false)]
        [Relation.OneToMany<SelfReferencing>.WithNavigation("Children", Inverse = "Parent")]
        public partial class SelfReferencing : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>Relation that crosses a boundary: FK yes, navigation no.</summary>
        public sealed class OtherBoundary;

        [Entity]
        [BelongsTo<OtherBoundary>]
        public partial class Faraway : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<Faraway>]
        [Relation.ManyToOne<Plain>]
        public partial class ReachesAcrossBoundary : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>A profile on an entity with one relation inside and one outside the boundary.</summary>
        [LoadWith<ReachesAcrossBoundary>(MaxDepth = 1)]
        [Query<ReachesAcrossBoundary, ReachesAcrossBoundaryDto>]
        public partial class LoadAcrossBoundary
        {
            [Filter]
            public Guid? Id { get; init; }
        }

        // ── domain key × soft delete ──────────────────────────────────────────────────────────────

        /// <summary>
        ///     The unique index on the logic key becomes PARTIAL when the entity is soft-delete, so a
        ///     deleted row does not prevent re-inserting the same code. Nobody would try this pair
        ///     without knowing it exists.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [SoftDelete]
        public partial class SoftDeletedWithLogicKey : IEntity
        {
            [LogicKey]
            public string Code { get; private set; } = "";
        }

        // ── filters that compose on the same entity ───────────────────────────────────────────────

        /// <summary>Two filters on the same type: soft delete (100) and ownership (200), in that order.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [SoftDelete]
        [HasOwner]
        public partial class SoftDeletedAndOwned : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── less common filter operators ──────────────────────────────────────────────────────────

        /// <summary>In on a collection, Between, a nested path, two sorts with priority.</summary>
        [Query<RelationChild, RelationChildDto>]
        public partial class RichFilterQuery
        {
            [Filter(Operator = FilterOperator.In, MapTo = "PersistenceId")]
            public List<Guid>? Ids { get; init; }

            // A range is two properties over one column — FilterOperator.Between is PRAG0701.
            [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Amount")]
            public decimal? MinAmount { get; init; }

            [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "Amount")]
            public decimal? MaxAmount { get; init; }

            [Filter(MapTo = "RelationParent.Name", IgnoreCase = true)]
            public string? ParentName { get; init; }

            [Sort(Priority = 0, DefaultDirection = SortDirection.Ascending, MapTo = "Amount")]
            public SortDirection? AmountSort { get; init; }

            [Sort(Priority = 1, DefaultDirection = SortDirection.Descending, MapTo = "PersistenceId")]
            public SortDirection? IdSort { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        // ── value object ──────────────────────────────────────────────────────────────────────────

        /// <summary>Validate decides Create's signature: the generated code mirrors it.</summary>
        [ValueObject]
        public partial record Money
        {
            public decimal Amount { get; init; }
            public string Currency { get; init; } = "";

            private static Money Validate(decimal amount, string currency) => new() { Amount = amount, Currency = currency };
        }

        /// <summary>Used as a property: EF maps it as a complex type, flattened into columns.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithValueObject : IEntity
        {
            public Money Price { get; private set; } = new();
        }

        // ── default calcolati e cascade ───────────────────────────────────────────────────────────

        public sealed class SerialGenerator : Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<WithComputedDefault, string>
        {
            public Task<string> GenerateAsync(
                WithComputedDefault entity,
                Pragmatic.Persistence.Lifecycle.LifecycleContext context,
                System.Threading.CancellationToken ct) => Task.FromResult("SER-1");
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class WithComputedDefault : IEntity
        {
            [ComputedDefault<WithComputedDefault, string, SerialGenerator>]
            public string Serial { get; private set; } = "";
        }

        /// <summary>The source raises the event; the target, in another assembly, listens to it.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class CascadeSourceEntity : IEntity
        {
            [CascadeSource]
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<CascadeSourceEntity>]
        public partial class CascadeTargetEntity : IEntity
        {
            // The handler filters its rows on the key the declared relation generates.

            [CascadeOn<CascadeSourceEntity>(nameof(CascadeSourceEntity.Name))]
            public string SourceName { get; private set; } = "";
        }

        // ── preset ────────────────────────────────────────────────────────────────────────────────

        public sealed class DefaultPresets : Pragmatic.Persistence.Lifecycle.IPresetProvider<WithPresets>
        {
            public Task<IReadOnlyList<object>> CreatePresetsAsync(
                WithPresets parent,
                Pragmatic.Persistence.Lifecycle.LifecycleContext context,
                System.Threading.CancellationToken ct)
                => Task.FromResult<IReadOnlyList<object>>(new List<object>());
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        [HasPresets]
        [PresetProvider<DefaultPresets>(Order = 0)]
        public partial class WithPresets : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // ── resource: CRUD scaffoldato ────────────────────────────────────────────────────────────

        /// <summary>An entity declared an API resource: actions, DTOs and endpoints scaffolded.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Resource("widgets", Capabilities = ResourceCapabilities.All)]
        public partial class Widget : IEntity
        {
            public string Name { get; private set; } = "";
        }

        /// <summary>
        ///     A hand-written mutation that answers with the <c>{E}ReadDto</c> that <c>[Resource]</c>
        ///     scaffolds for the same entity.
        /// </summary>
        /// <remarks>
        ///     ⚠️ The DTO carries its <c>[MapFrom]</c> in generated code, and a generator reads the
        ///     compilation it received, not what another one is about to add — so querying the symbol
        ///     would answer "no FromEntity" for a type that has one, and <c>PRAG0531</c> would tell the
        ///     author to annotate a file they do not own. If the case breaks, this corpus stops
        ///     compiling: the most direct way it has to say so.
        /// </remarks>
        [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
        [ReturnsDto<WidgetReadDto>]
        [Pragmatic.Endpoints.Attributes.Endpoint(
            Pragmatic.Endpoints.HttpVerb.Put, "api/widgets/{id}/name")]
        public partial class RenameWidgetMutation : Pragmatic.Actions.Mutation.Mutation<Widget>
        {
            public required Guid Id { get; init; }
            public required string Name { get; init; }
        }

        /// <summary>
        ///     The two declarations contradict each other: "written through the parent" and "API resource
        ///     with its own CRUD". Nothing reports it yet — see ResourceOnAPartOfEntityScaffoldsCrudAnyway.
        /// </summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Relation.ManyToOne<RelationParent>.WithNavigation("Parent", OnDelete = DeleteBehavior.Cascade)]
        [PartOf<RelationParent>]
        [Resource("contradictions", Capabilities = ResourceCapabilities.All)]
        public partial class ContradictoryPart : IEntity
        {
            public string Description { get; private set; } = "";
        }

        // ── inheritance × trait ───────────────────────────────────────────────────────────────────

        /// <summary>A hierarchy whose members also carry traits: two mechanisms on the same type.</summary>
        [Entity]
        [BelongsTo<InventoryBoundary>]
        [Inheritance(InheritanceStrategy.Tpt)]
        [Auditable]
        [SoftDelete]
        public abstract partial class TraitedBase : IEntity
        {
            public decimal Amount { get; private set; }
        }

        [Entity]
        [BelongsTo<InventoryBoundary>]
        public partial class TraitedDerived : TraitedBase
        {
            public string Reason { get; private set; } = "";
        }

        public static class Program
        {
            public static void Main() { }
        }
        """;
}
