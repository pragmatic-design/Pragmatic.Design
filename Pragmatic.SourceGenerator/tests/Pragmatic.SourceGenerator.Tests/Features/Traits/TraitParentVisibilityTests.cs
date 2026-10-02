using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     PRAG2607: a trait on a parent whose rows are restricted per caller.
/// </summary>
/// <remarks>
///     The generated list query filters children by the parent id it was handed and checks the
///     trait's own read permission — it never asks whether the caller may see that parent, and the
///     child entity carries no restriction of its own. A caller holding
///     <c>…attachments.read</c> can therefore enumerate the attachments of a case file it cannot
///     open, learning file names, dates and uploaders.
///     <para>
///         Found independently by the consumer who built the application and by a review of it, and
///         it breaks the requirement that a non-assigned surveyor "must not even know it exists".
///         The warning is a mitigation, not a fix: it makes the gap visible at compile time while the
///         question of how the filter pipeline should treat the parent navigation is settled.
///     </para>
/// </remarks>
public class TraitParentVisibilityTests
{
    private const string Template = """
        using Pragmatic.Attachments;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Surveys.Files
        {
            public sealed class SurveysBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<SurveysBoundary>]
            [Resource("case-files")]
            __PROTECTION__
            [HasAttachments]
            public partial class CaseFile : IEntity
            {
                public System.Guid Id { get; set; }
                public System.Guid PersistenceId { get => Id; set => Id = value; }
                public string Number { get; set; } = string.Empty;
            }

            [PragmaticDbContext("Surveys")]
            public partial class SurveysDbContext { }
        }
        """;

    private static string Source(string protection) => Template.Replace("__PROTECTION__", protection);

    [Theory]
    [InlineData("[HasAccessScopes]", "AccessScopes.Any(s => userScopes.Contains(s))")]
    [InlineData("[HasOwner]", "OwnerId == userId")]
    public void RowRestrictedParent_GetsAFilterCarryingTheParentPredicate(string protection, string expected)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source(protection));

        var filter = Filter(sources);

        filter.Should().Contain("ParentVisibilityFilter");
        filter.Should().Contain(expected, "the predicate is the parent's own, read through the navigation");
        filter.Should().Contain("entity.CaseFile != null",
            "a child whose parent row is gone must not be visible either");
        filter.Should().Contain("surveys.case-file.view-all",
            "whoever may see every parent may list every child — the parent's own answer");
    }

    [Fact]
    public void ParentWithBothRestrictions_CombinesThemWithOr()
    {
        var (sources, _) = TraitCompilationHarness.Generate(
            Source("[HasOwner]\n            [HasAccessScopes]"));

        Filter(sources).Should().Contain("||", "ownership OR scopes, exactly as the parent's own filter");
    }

    [Fact]
    public void UnrestrictedParent_GetsNoFilter()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source(string.Empty));

        Filter(sources).Should().BeEmpty(
            "with no per-caller restriction on the parent there is nothing to inherit");
    }

    /// <summary>
    ///     The filter has to be registered, not merely generated — the failure mode this whole change
    ///     exists to close, and the one that already bit the computed data-scope filter.
    /// </summary>
    [Fact]
    public void Filter_IsRegisteredWithTheQueryFilters()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source("[HasAccessScopes]"));

        var registration = string.Join("\n", sources
            .Where(s => s.Key.Contains("QueryFilters"))
            .Select(s => s.Value));

        registration.Should().Contain("CaseFileAttachment.ParentVisibilityFilter");
        registration.Should().Contain("TryAddEnumerable",
            "IQueryFilter is a collection — TryAdd would silently skip it");
    }

    private static string Filter(System.Collections.Generic.IReadOnlyDictionary<string, string> sources)
        => string.Join("\n", sources
            .Where(s => s.Key.Contains("ParentVisibilityFilter"))
            .Select(s => s.Value));
}
