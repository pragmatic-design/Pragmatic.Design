using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[TransitionsTo&lt;TState&gt;(target)]</c> is performed by the generated invoker — before the body,
///     after it, or checked after a body that performs it — and the declarations it cannot perform are
///     errors.
/// </summary>
/// <remarks>
///     The generator reads the attribute, so no body calls <c>TransitionTo</c> by hand beside it. What the overrides do at run
///     time is <c>AnOperationMovesItsEntityWhereItDeclaresTests</c>' to say, in Pragmatic.Actions.Tests,
///     and the example applications' container suites run them end to end.
/// </remarks>
public sealed class TheInvokerPerformsTheDeclaredTransitionTests
{
    private const string Domain = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.StateMachine;
        using Pragmatic.Result;

        namespace Booking
        {
            public enum OtherStatus { Open, Done }

            public enum ReservationStatus
            {
                [InitialState] Pending,
                [TransitionFrom(ReservationStatus.Pending)] Confirmed,
                [TransitionFrom(ReservationStatus.Pending)] [TransitionFrom(ReservationStatus.Confirmed)] Cancelled
            }

            [Entity]
            [StateMachine<ReservationStatus>]
            public partial class Reservation : IEntity
            {
                public Guid PersistenceId { get; set; }
                public ReservationStatus Status { get; private set; }
            }

            [Entity]
            public partial class Guest : IEntity
            {
                public Guid PersistenceId { get; set; }
            }
        """;

    private static string Mutation(string attributes, string body = "", string mode = "Update") => Domain + $$"""

            [Mutation(Mode = MutationMode.{{mode}})]
            {{attributes}}
            public partial class ConfirmReservation : Mutation<Reservation>
            {
                public required Guid Id { get; init; }
                {{body}}
            }
        }
        """;

    private static string Action(string attributes, string loads, string body) => Domain + $$"""

            [DomainAction]
            {{loads}}
            {{attributes}}
            public partial class ConfirmByAction : VoidDomainAction
            {
                public required Guid Id { get; init; }
                public Guid GuestId { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                {
                    {{body}}
                    return Task.FromResult(VoidResult<IError>.Success());
                }
            }
        }
        """;

    private const string Confirmed = "[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed)]";

    private const string ManualTransition = """
        public override Task<Result<Reservation, IError>> ApplyAsync(Reservation entity, CancellationToken ct = default)
        {
            var moved = entity.TransitionTo(ReservationStatus.Confirmed);
            return Task.FromResult(moved.IsFailure
                ? Result<Reservation, IError>.Failure(moved.Error)
                : Result<Reservation, IError>.Success(entity));
        }
        """;

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Persistence.StateMachine.StateMachineAttribute<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        ]);

    private static string MutationInvoker(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSource(result, "ConfirmReservation.MutationInvoker")
           ?? throw new InvalidOperationException("No mutation invoker was generated.");

    private static string ActionInvoker(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSource(result, "ConfirmByAction.Invoker")
           ?? throw new InvalidOperationException("No action invoker was generated; generated: "
               + string.Join(", ", GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys));

    private static int Count(SourceGenRunResult result, string id)
        => GeneratorTestHelper.GetDiagnosticsById(result, id).Count();

    [Fact]
    public void ByDefault_TheMutationInvokerTransitionsBeforeTheBody()
    {
        var invoker = MutationInvoker(Run(Mutation(Confirmed)));

        invoker.Should().Contain("protected override global::Pragmatic.Result.IError? TransitionBeforeBody(global::Booking.Reservation entity)")
            .And.Contain("entity.TransitionTo(global::Booking.ReservationStatus.Confirmed)")
            .And.Contain("return __moved.IsFailure ? __moved.Error : null;");
    }

    [Fact]
    public void AfterBody_TheMutationInvokerTransitionsAfterIt()
    {
        var invoker = MutationInvoker(Run(Mutation(
            "[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed, When = TransitionTiming.AfterBody)]")));

        invoker.Should().Contain("TransitionAfterBody(global::Booking.Reservation entity)")
            .And.NotContain("TransitionBeforeBody");
    }

    [Fact]
    public void ByBody_TheInvokerChecksTheBodyMovedIt()
    {
        var invoker = MutationInvoker(Run(Mutation(
            "[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed, When = TransitionTiming.ByBody)]",
            ManualTransition)));

        invoker.Should().Contain("protected override void EnsureTheBodyTransitioned(global::Booking.Reservation entity)")
            .And.Contain("if (entity.Status != global::Booking.ReservationStatus.Confirmed)")
            .And.NotContain(".TransitionTo(");
    }

    /// <summary>A conditional transition is declared for the contract tests and nothing else.</summary>
    [Fact]
    public void AConditionalTransition_IsNeitherPerformedNorChecked()
    {
        var invoker = MutationInvoker(Run(Mutation(
            "[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed, When = TransitionTiming.ByBody, IsConditional = true)]",
            ManualTransition)));

        invoker.Should().NotContain("TransitionBeforeBody").And.NotContain("EnsureTheBodyTransitioned");
    }

    /// <summary>The control: no attribute, no transition.</summary>
    [Fact]
    public void WithoutTheAttribute_NothingIsGenerated()
    {
        var result = Run(Mutation("", ManualTransition));

        MutationInvoker(result).Should().NotContain("TransitionBeforeBody")
            .And.NotContain("TransitionAfterBody").And.NotContain("EnsureTheBodyTransitioned");
        Count(result, "PRAG0466").Should().Be(0, "without the attribute the body's call is the only one");
    }

    [Fact]
    public void ABodyThatStillTransitions_IsPRAG0466()
    {
        var result = Run(Mutation(Confirmed, ManualTransition));

        Count(result, "PRAG0466").Should().Be(1, "the second call would be refused as Confirmed → Confirmed");
        MutationInvoker(result).Should().NotContain("TransitionBeforeBody",
            "a declaration reported as wrong is not performed on top of the body's call");
    }

    /// <summary>The control: the same body under ByBody is the correct declaration.</summary>
    [Fact]
    public void TheSameBodyUnderByBody_IsNotReported()
        => Count(Run(Mutation(
                "[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed, When = TransitionTiming.ByBody)]",
                ManualTransition)), "PRAG0466")
            .Should().Be(0);

    [Fact]
    public void OnACreateMutation_IsPRAG0468()
        => Count(Run(Mutation(Confirmed, mode: "Create").Replace("public required Guid Id { get; init; }", "")),
                "PRAG0468")
            .Should().Be(1);

    [Fact]
    public void AnEnumTheEntityDoesNotGovern_IsPRAG0465()
        => Count(Run(Mutation("[TransitionsTo<OtherStatus>(OtherStatus.Done)]")), "PRAG0465").Should().Be(1);

    [Fact]
    public void AnAction_TransitionsItsLoadedEntityBeforeTheBody()
    {
        var invoker = ActionInvoker(Run(Action(Confirmed, "[LoadEntity<Reservation>(nameof(Id))]", "")));

        invoker.Should().Contain("protected override global::Pragmatic.Result.IError? TransitionBeforeBody(global::Booking.ConfirmByAction action)")
            .And.Contain("action._reservation.TransitionTo(global::Booking.ReservationStatus.Confirmed)");
    }

    /// <summary>The entity is the one whose state machine the attribute names, not any loaded row.</summary>
    [Fact]
    public void AnAction_PicksTheLoadedEntityWithThatStateMachine()
    {
        var invoker = ActionInvoker(Run(Action(Confirmed,
            "[LoadEntity<Guest>(nameof(GuestId))]\n[LoadEntity<Reservation>(nameof(Id))]", "")));

        invoker.Should().Contain("action._reservation.TransitionTo(").And.NotContain("action._guest.TransitionTo(");
    }

    [Fact]
    public void AnActionWithNoSuchLoadedEntity_IsPRAG0465()
        => Count(Run(Action(Confirmed, "[LoadEntity<Guest>(nameof(GuestId))]", "")), "PRAG0465").Should().Be(1);

    [Fact]
    public void AfterBodyOnAnAction_IsPRAG0467()
        => Count(Run(Action(
                "[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed, When = TransitionTiming.AfterBody)]",
                "[LoadEntity<Reservation>(nameof(Id))]", "")), "PRAG0467")
            .Should().Be(1);
}
