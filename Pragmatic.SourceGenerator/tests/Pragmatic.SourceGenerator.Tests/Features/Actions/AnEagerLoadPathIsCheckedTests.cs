using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[EagerLoad("path")]</c> on a mutation is checked against its entity as <c>[LoadEntity(Include)]</c> is: a path
///     that names no navigation is <c>PRAG0453</c>, and is not emitted.
/// </summary>
/// <remarks>
///     The path was copied into the generated <c>LoadEntityAsync</c> as an EF Core <c>Include</c> and never
///     checked: a typo, or a scalar, was an exception at the first request.
/// </remarks>
public class AnEagerLoadPathIsCheckedTests
{
    private static string Model(string eagerLoad) => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

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

            [Mutation(Mode = MutationMode.Update)]
            {{eagerLoad}}
            public partial class RenameTeamMutation : Mutation<Team>
            {
                public required Guid Id { get; init; }
                public string Name { get; init; } = "";
            }
        }
        """;

    [Theory]
    [InlineData("Membres", "Membres")]
    [InlineData("Name", "Name")]
    [InlineData("Members.Nickname", "Nickname")]
    public void APathThatNamesNoNavigation_IsReported_AndNotLoaded(string path, string segment)
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model($"[EagerLoad(\"{path}\")]"));

        diagnostics.Where(d => d.Id == "PRAG0453").Select(d => d.GetMessage())
            .Should().ContainSingle(m => m.Contains("RenameTeamMutation") && m.Contains($"'{segment}'"));
        Invoker(sources).Should().NotContain($"\"{path}\"", "a path EF Core would refuse is not handed to it");
    }

    /// <summary>The control: a path through a navigation a <c>[Relation]</c> generates reports nothing and is loaded.</summary>
    [Fact]
    public void APathThroughAGeneratedNavigation_IsLoaded()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model("[EagerLoad(\"Members\")]"));

        diagnostics.Should().NotContain(d => d.Id == "PRAG0453");
        Invoker(sources).Should().Contain("\"Members\"");
    }

    private static string Invoker(System.Collections.Generic.IReadOnlyDictionary<string, string> sources)
    {
        var match = sources.FirstOrDefault(s => s.Key.Contains("RenameTeamMutation.MutationInvoker"));
        match.Value.Should().NotBeNull($"the invoker is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
