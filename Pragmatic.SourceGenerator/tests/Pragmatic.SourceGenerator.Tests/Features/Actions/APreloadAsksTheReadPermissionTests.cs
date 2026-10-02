using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>RequireReadPermission = true</c> on a load: the caller must hold the entity's read permission —
///     the one its CRUD constants carry — and is refused 403 before anything is read.
/// </summary>
/// <remarks>
///     A load is authorized by the operation's own permission and the row filters, nothing else;
///     without this flag an operation that hands back what it loaded, and wants the caller to be one who
///     may read it, has no way to say so.
/// </remarks>
public class APreloadAsksTheReadPermissionTests
{
    private static string Model(string arguments) => $$"""
        using System;
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
            public partial class Team : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }

            [PragmaticDbContext("People")]
            public partial class PeopleDbContext { }

            [DomainAction]
            [LoadEntity<Team>(nameof(TeamId){{arguments}})]
            public partial class NameTeamAction : DomainAction<string>
            {
                public required Guid TeamId { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<string, IError>>(_team.Name);
            }
        }
        """;

    [Fact]
    public void TheEntitysReadPermission_IsAskedBeforeTheLoad()
    {
        var invoker = Invoker(Model(", RequireReadPermission = true"));

        var asked = invoker.IndexOf("global::Pragmatic.Actions.Pipeline.PreloadAuthorization.RequireAllAsync(", System.StringComparison.Ordinal);
        asked.Should().BeGreaterThan(-1);
        invoker.Should().Contain("\"people.team.read\"", "the value the entity's CRUD Read constant carries");
        invoker.IndexOf("GetByIdAsync(", System.StringComparison.Ordinal).Should().BeGreaterThan(asked,
            "a caller who may not read the rows is refused before they are read");
    }

    /// <summary>The control: without it, no permission beyond the operation's is asked.</summary>
    [Fact]
    public void WithoutIt_NoPermissionIsAsked()
    {
        Invoker(Model("")).Should().NotContain("PreloadAuthorization");
    }

    [Fact]
    public void TheAskingLoad_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model(", RequireReadPermission = true"),
            static path => path.Contains("NameTeamAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string Invoker(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var match = sources.FirstOrDefault(s => s.Key.Contains("NameTeamAction.Invoker"));
        match.Value.Should().NotBeNull($"the invoker is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
