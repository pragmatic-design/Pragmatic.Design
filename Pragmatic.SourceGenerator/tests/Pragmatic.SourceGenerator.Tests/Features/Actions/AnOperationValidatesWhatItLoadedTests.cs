using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>ValidateLoaded()</c> / <c>ValidateLoadedAsync(CancellationToken)</c> on an operation: the invoker
///     calls it right after the preload, before the body, and a failure is the 422 validation is.
/// </summary>
/// <remarks>
///     A rule that needs the preloaded entity could not be validation without reading the row a
///     second time: <c>[LoadEntity]</c> loads after validation, and a validator cannot see the operation's
///     loaded fields. Time off's submission read the kind of absence twice for that reason.
/// </remarks>
public class AnOperationValidatesWhatItLoadedTests
{
    private const string Model = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;
        using Pragmatic.Validation.Types;

        namespace TestApp
        {
            [Boundary]
            public partial class LeaveBoundary { }

            [Entity]
            [BelongsTo<LeaveBoundary>]
            public partial class Kind : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public bool CountsHours { get; set; }
            }

            [Entity]
            [BelongsTo<LeaveBoundary>]
            public partial class Request : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string? Note { get; set; }
            }

            [PragmaticDbContext("Leave")]
            public partial class LeaveDbContext { }

            [DomainAction]
            [LoadEntity<Kind>(nameof(KindId))]
            public partial class AskAction : DomainAction<bool>
            {
                public required Guid KindId { get; init; }
                public decimal? Hours { get; init; }

                private ValidationError ValidateLoaded()
                    => _kind.CountsHours && Hours is null
                        ? ValidationError.For(nameof(Hours), "hours.required")
                        : ValidationError.Valid;

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }

            [Mutation(Mode = MutationMode.Update)]
            [LoadEntity<Kind>(nameof(KindId))]
            public partial class RetypeRequestMutation : Mutation<Request>
            {
                public required Guid Id { get; init; }
                public required Guid KindId { get; init; }

                private Task<ValidationError> ValidateLoadedAsync(CancellationToken ct)
                    => Task.FromResult(_kind.CountsHours ? ValidationError.For(nameof(KindId), "kind.hours") : ValidationError.Valid);
            }

            [DomainAction]
            [LoadEntity<Kind>(nameof(KindId))]
            public partial class LookAction : DomainAction<bool>
            {
                public required Guid KindId { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
        }
        """;

    [Fact]
    public void AnAction_ValidatesAfterThePreload()
    {
        var invoker = Invoker("AskAction.Invoker");

        var loaded = invoker.IndexOf("action.SetLoadedEntities(", System.StringComparison.Ordinal);
        var validated = invoker.IndexOf("action.ValidateLoaded()", System.StringComparison.Ordinal);
        loaded.Should().BeGreaterThan(-1);
        validated.Should().BeGreaterThan(loaded, "the rule reads what the preload loaded");
        invoker.Should().Contain(".IsFailure)");
    }

    [Fact]
    public void AMutation_AwaitsTheAsyncForm()
        => Invoker("RetypeRequestMutation.MutationInvoker")
            .Should().Contain("await mutation.ValidateLoadedAsync(ct)");

    /// <summary>The control: an operation that declares no such method validates nothing after the load.</summary>
    [Fact]
    public void AnOperationWithoutIt_CallsNothing()
        => Invoker("LookAction.Invoker").Should().NotContain("ValidateLoaded");

    [Fact]
    public void TheOperations_Compile()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model,
            static path => path.Contains("AskAction") || path.Contains("RetypeRequestMutation")
                           || path.Contains("LookAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    [Fact]
    public void AMethodOfThatNameWithAnotherShape_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model.Replace(
            "private ValidationError ValidateLoaded()",
            "private bool ValidateLoaded()").Replace(
            """
                        ? ValidationError.For(nameof(Hours), "hours.required")
                        : ValidationError.Valid;
            """,
            """
                        ? false
                        : true;
            """));

        diagnostics.Where(d => d.Id == "PRAG0452").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("AskAction") && m.Contains("ValidateLoaded"));
    }

    private static string Invoker(string hint)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model);
        var match = sources.FirstOrDefault(s => s.Key.Contains(hint));
        match.Value.Should().NotBeNull($"{hint} is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
