using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[LoadEntity(Include = "…")]</c>: the preloaded entity comes with the navigations named, and a path
///     that names no navigation is a build error.
/// </summary>
/// <remarks>
///     <c>[LoadEntity]</c> read the row alone and there is no lazy loading, so a navigation of the
///     preloaded entity was an empty collection, silently — Time off's team grant read the members with a
///     second query instead.
/// </remarks>
public class ALoadedEntityCarriesItsNavigationsTests
{
    private static string Model(string include) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class PeopleBoundary { }

            [Entity]
            [BelongsTo<PeopleBoundary>]
            [Relation.OneToMany<Member>.WithNavigation("Members", Inverse = "Team")]
            public partial class Team : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }

            [Entity]
            [BelongsTo<PeopleBoundary>]
            public partial class Member : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }

            [PragmaticDbContext("People")]
            public partial class PeopleDbContext { }

            [DomainAction]
            [LoadEntity<Team>(nameof(TeamId){{include}})]
            public partial class CountMembersAction : DomainAction<int>
            {
                public required Guid TeamId { get; init; }

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<int, IError>>(_team.Members.Count);
            }
        }
        """;

    [Fact]
    public void AnInclude_LoadsTheNavigationWithTheRow()
    {
        var invoker = Invoker(Model(", Include = \"Members\""));

        invoker.Should().Contain("Include(");
        invoker.Should().Contain("\"Members\"");
        invoker.Should().Contain("FirstOrDefaultAsync(");
    }

    /// <summary>The control: without an include the row is read as before, alone.</summary>
    [Fact]
    public void WithoutAnInclude_TheRowIsReadAlone()
    {
        var invoker = Invoker(Model(""));

        invoker.Should().Contain("GetByIdAsync(action.TeamId, ct)");
        invoker.Should().NotContain("Include(");
    }

    [Fact]
    public void TheIncludedLoad_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model(", Include = \"Members\""),
            static path => path.Contains("CountMembersAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    [Theory]
    [InlineData("Membres", "Membres")]
    [InlineData("Name", "Name")]
    [InlineData("Members.Nickname", "Nickname")]
    public void APathThatNamesNoNavigation_IsReported(string path, string segment)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model($", Include = \"{path}\""));

        diagnostics.Where(d => d.Id == "PRAG0453").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("CountMembersAction") && m.Contains($"'{segment}'"));
    }

    private static string Invoker(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var match = sources.FirstOrDefault(s => s.Key.Contains("CountMembersAction.Invoker"));
        match.Value.Should().NotBeNull($"the invoker is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
