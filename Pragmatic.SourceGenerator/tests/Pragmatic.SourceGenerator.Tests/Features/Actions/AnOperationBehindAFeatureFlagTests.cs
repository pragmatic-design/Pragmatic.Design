using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[RequiresFeature]</c> on an action and a mutation (#747): the invoker asks <c>IFeatureFlags</c> before
///     the body and refuses with a 404, and the preparation hook it adds to compiles.
/// </summary>
/// <remarks>
///     The query is proved over HTTP in the Showcase (<c>FeatureFlagTests</c>); these pin the two invokers that
///     write the check in their preparation hook, which becomes <c>async</c> for it.
/// </remarks>
public class AnOperationBehindAFeatureFlagTests
{
    private const string Model = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.FeatureFlags;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class CasesBoundary { }

            [Entity]
            [BelongsTo<CasesBoundary>]
            public partial class Case : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Subject { get; set; } = "";
            }

            public sealed class Escalations : IFeatureFlag
            {
                public static string Name => "escalations";
                public static string? Description => null;
            }

            [Mutation(Mode = MutationMode.Create)]
            [RequiresFeature<Escalations>]
            public partial class OpenCaseMutation : Mutation<Case>
            {
                public required string Subject { get; init; }
            }

            [DomainAction]
            [BelongsTo<CasesBoundary>]
            [RequiresFeature("escalations")]
            [RequiresFeature("audits")]
            public partial class EscalateAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<string, IError>>("escalated");
            }
        }
        """;

    [Fact]
    public void TheMutation_AsksForItsFlagByType_AndRefusesWithA404()
    {
        var invoker = Source("OpenCaseMutation.MutationInvoker");

        invoker.Should().Contain("IsEnabledAsync<global::TestApp.Escalations>(ct)");
        invoker.Should().Contain("NotFoundError.Create(\"Operation\")");
    }

    [Fact]
    public void TheAction_AsksForEveryFlag()
    {
        var invoker = Source("EscalateAction.Invoker");

        invoker.Should().Contain("IsEnabledAsync(\"escalations\", ct)");
        invoker.Should().Contain("IsEnabledAsync(\"audits\", ct)");
    }

    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model,
            static path => path.Contains("OpenCaseMutation") || path.Contains("EscalateAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string Source(string hint)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model);
        var match = sources.FirstOrDefault(s => s.Key.Contains(hint));
        match.Value.Should().NotBeNull($"generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
