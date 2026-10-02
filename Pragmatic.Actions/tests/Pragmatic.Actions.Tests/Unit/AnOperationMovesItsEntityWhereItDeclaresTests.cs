using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Where the pipelines run the transition <c>[TransitionsTo]</c> declares: before the body, after it,
///     or — with <c>ByBody</c> — the check that the body did it.
/// </summary>
/// <remarks>
///     The invokers here override the hooks by hand, in the shape the generator emits
///     (<c>TheInvokerPerformsTheDeclaredTransitionTests</c> pins that shape): what is measured is the
///     pipeline's order — what the body sees, which refusal answers, what is saved.
/// </remarks>
public sealed class AnOperationMovesItsEntityWhereItDeclaresTests
{
    private enum Status { Pending, Confirmed, Cancelled }

    /// <summary>A two-move state machine, the shape the generator gives an entity.</summary>
    private sealed class Reservation
    {
        public Status Status { get; private set; } = Status.Pending;

        public VoidResult<IError> TransitionTo(Status target)
        {
            if (Status != Status.Pending || target == Status.Pending)
                return VoidResult<IError>.Failure(new ConflictError
                {
                    EntityType = nameof(Reservation), Reason = $"Cannot transition from '{Status}' to '{target}'."
                });

            Status = target;
            return VoidResult<IError>.Success();
        }
    }

    private sealed class NotYoursError : IError
    {
        public string Code => "NOT_YOURS";
        public int StatusCode => 403;
        public string Title => Code;
    }

    private sealed class Confirm : Mutation<Reservation>
    {
        public IError? Refuse { get; init; }
        public bool MoveItself { get; init; }
        public int Runs { get; private set; }
        public Status? SeenByTheBody { get; private set; }

        public override Task<Result<Reservation, IError>> ApplyAsync(Reservation entity, CancellationToken ct = default)
        {
            Runs++;
            SeenByTheBody = entity.Status;

            if (Refuse is not null)
                return Task.FromResult(Result<Reservation, IError>.Failure(Refuse));

            if (MoveItself)
                entity.TransitionTo(Status.Confirmed);

            return Task.FromResult<Result<Reservation, IError>>(entity);
        }
    }

    private enum Timing { BeforeBody, AfterBody, ByBody }

    /// <summary>What the generator emits for [TransitionsTo(Confirmed, When = timing)].</summary>
    private sealed class ConfirmInvoker(IServiceProvider sp, Timing timing) : MutationInvoker<Confirm, Reservation>(sp)
    {
        public Reservation Stored { get; } = new();
        public int Saves { get; private set; }

        protected override void InjectDependencies(Confirm mutation) { }
        protected override Task<Reservation?> LoadEntityAsync(Confirm mutation, CancellationToken ct)
            => Task.FromResult<Reservation?>(Stored);
        protected override Reservation CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Update;
        protected override string? GetEntityIdString(Confirm mutation) => "r-1";
        protected override void PersistNew(Reservation entity) { }
        protected override void DeleteEntity(Reservation entity) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }

        protected override IError? TransitionBeforeBody(Reservation entity)
            => timing == Timing.BeforeBody ? Move(entity) : null;

        protected override IError? TransitionAfterBody(Reservation entity)
            => timing == Timing.AfterBody ? Move(entity) : null;

        protected override void EnsureTheBodyTransitioned(Reservation entity)
        {
            if (timing == Timing.ByBody && entity.Status != Status.Confirmed)
                throw new InvalidOperationException($"left Status at '{entity.Status}'");
        }

        private static IError? Move(Reservation entity)
        {
            var moved = entity.TransitionTo(Status.Confirmed);
            return moved.IsFailure ? moved.Error : null;
        }
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new ValidationOptions()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task BeforeBody_TheBodySeesTheNewState_AndTheRowIsSaved()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.BeforeBody);
        var mutation = new Confirm();

        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        mutation.SeenByTheBody.Should().Be(Status.Confirmed);
        invoker.Stored.Status.Should().Be(Status.Confirmed);
        invoker.Saves.Should().Be(1);
    }

    [Fact]
    public async Task BeforeBody_ARefusedMove_Answers409_AndNeitherRunsTheBodyNorSaves()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.BeforeBody);
        invoker.Stored.TransitionTo(Status.Cancelled);
        var mutation = new Confirm();

        var result = await invoker.InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
        mutation.Runs.Should().Be(0, "a refused move answers before any of the body's work");
        invoker.Saves.Should().Be(0);
    }

    [Fact]
    public async Task AfterBody_TheBodySeesTheOldState_AndTheMoveFollowsIt()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.AfterBody);
        var mutation = new Confirm();

        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        mutation.SeenByTheBody.Should().Be(Status.Pending);
        invoker.Stored.Status.Should().Be(Status.Confirmed);
    }

    /// <summary>The reason AfterBody exists: the body's own refusal keeps its own error.</summary>
    [Fact]
    public async Task AfterBody_ARefusingBody_AnswersWithItsOwnError_AndNothingMoves()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.AfterBody);

        var result = await invoker.InvokeAsync(new Confirm { Refuse = new NotYoursError() });

        result.Error.Should().BeOfType<NotYoursError>();
        invoker.Stored.Status.Should().Be(Status.Pending);
    }

    [Fact]
    public async Task ByBody_ABodyThatMovesTheEntity_Succeeds()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.ByBody);

        var result = await invoker.InvokeAsync(new Confirm { MoveItself = true });

        result.IsSuccess.Should().BeTrue();
        invoker.Stored.Status.Should().Be(Status.Confirmed);
    }

    /// <summary>A declaration the body does not honour is a programming error, and it is loud.</summary>
    [Fact]
    public async Task ByBody_ABodyThatDoesNotMoveIt_Throws_AndNothingIsSaved()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.ByBody);

        var act = async () => await invoker.InvokeAsync(new Confirm()).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
        invoker.Saves.Should().Be(0);
    }

    /// <summary>The check runs after a successful body only: a refusal is the body's answer.</summary>
    [Fact]
    public async Task ByBody_ARefusingBody_IsNotChecked()
    {
        using var sp = Services();
        var invoker = new ConfirmInvoker(sp, Timing.ByBody);

        var result = await invoker.InvokeAsync(new Confirm { Refuse = new NotYoursError() });

        result.Error.Should().BeOfType<NotYoursError>();
    }

    // ── A domain action: the loaded row, moved before the body builds its answer ─────────────────

    private sealed class ConfirmAction : VoidDomainAction
    {
        public Reservation Loaded { get; } = new();
        public int Runs { get; private set; }
        public Status? SeenByTheBody { get; private set; }

        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        {
            Runs++;
            SeenByTheBody = Loaded.Status;
            return Task.FromResult(VoidResult<IError>.Success());
        }
    }

    private sealed class ConfirmActionInvoker(IServiceProvider sp) : VoidDomainActionInvoker<ConfirmAction>(sp)
    {
        protected override void InjectDependencies(ConfirmAction action) { }

        protected override IError? TransitionBeforeBody(ConfirmAction action)
        {
            var moved = action.Loaded.TransitionTo(Status.Confirmed);
            return moved.IsFailure ? moved.Error : null;
        }
    }

    [Fact]
    public async Task AnAction_MovesItsEntityBeforeTheBody()
    {
        using var sp = Services();
        var action = new ConfirmAction();

        var result = await new ConfirmActionInvoker(sp).InvokeAsync(action);

        result.IsSuccess.Should().BeTrue();
        action.SeenByTheBody.Should().Be(Status.Confirmed, "the body builds the answer, so it must see the new state");
    }

    [Fact]
    public async Task AnAction_ARefusedMove_Answers409_WithoutRunningTheBody()
    {
        using var sp = Services();
        var action = new ConfirmAction();
        action.Loaded.TransitionTo(Status.Cancelled);

        var result = await new ConfirmActionInvoker(sp).InvokeAsync(action);

        result.Error.Should().BeOfType<ConflictError>();
        action.Runs.Should().Be(0);
    }
}
