using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Saga;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Reservations.Sagas;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     What the saga orchestrator leaves in the store when a saga ends without completing, read through the store's own repository.
/// </summary>
/// <remarks>
///     The status a saga ends in (compensated, timed out) is read through <see cref="ISagaRepository{TSaga}.GetStatusAsync" /> (#884), so
///     these run on every engine: EF Core's store, the Sql engine's and the documents engine's. The rest of the saga's flow is
///     <see cref="SagaFlowTests" />.
/// </remarks>
public class SagaPersistenceTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     The tenant a saga written outside a request belongs to — the same one the HTTP tests send in
    ///     <c>X-Tenant-Id</c>.
    /// </summary>
    /// <remarks>
    ///     <see cref="SagaInstance" /> is an <see cref="Pragmatic.MultiTenancy.ITenantEntity" /> by
    ///     design, so saga state is one organisation's and its read filter is fail-closed. A real
    ///     consumer arrives with a tenant — the transport carries it in a header and the consume scope
    ///     restores it — and these tests drive the handlers in-process, so they have to establish it
    ///     themselves. The write path refuses a row with no tenant, which would otherwise belong to
    ///     nobody — on a host with a database per tenant, the shared database.
    /// </remarks>
    private const string Tenant = "test-tenant";

    // E2E through the real orchestrator: an invalid identity must NOT advance to
    // RoomAssigned (the declared NextState must not overwrite the rejection), and the saga must be
    // marked Compensated (terminal) rather than left Active.
    [Fact]
    public async Task SagaFlow_EndToEnd_InvalidIdentity_CompensatesWithoutAdvancing()
    {
        using var tenant = TenantScope.BeginScope(Tenant);

        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        var correlationId = reservationId.ToString();

        await DispatchAsync(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));
        await AssertSagaStateAsync(correlationId, CheckInState.GuestVerified,
            "the start event must advance the saga to GuestVerified");

        // Invalid identity → Handle throws SagaRejectedException → orchestrator compensates, marks
        // Compensated, and does NOT rethrow. DispatchAsync completes normally (terminal, no retry).
        await DispatchAsync(new IdentityVerified(reservationId, IsValid: false));

        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();
        var saga = await repo.FindByCorrelationAsync(correlationId);
        saga.Should().NotBeNull();
        saga!.State.Should().NotBe(CheckInState.RoomAssigned,
            "the declared NextState must not overwrite the handler's rejection — the invalid guest must not advance");

        (await repo.GetStatusAsync(saga.Id)).Should().Be(SagaStatus.Compensated,
            "a rejected step must run compensation and leave the saga Compensated, not Active");
    }

    [Fact]
    public void SagaRepository_IsDurable_NotInMemory()
    {
        // BookingBoundary is [EnableSagaPersistence]: the SG wires the store of the boundary's engine (EF Core's, the Sql engine's or the
        // documents engine's) rather than the in-memory default, which is what lets saga state survive a process restart.
        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();

        repo.Should().NotBeOfType<InMemorySagaRepository<CheckInSaga>>();
    }

    [Fact]
    public async Task SagaFlow_TimeoutRunner_MarksTimedOut_AndRunsCompensationChain()
    {
        // Drive the saga to RoomAssigned (Handle(IdentityVerified) sets a 10-minute
        // [SagaTimeout] deadline). Then force the deadline into the past and invoke
        // the SG-generated ISagaTimeoutRunner directly — the compensation chain must
        // run and the instance must be marked TimedOut.
        using var tenant = TenantScope.BeginScope(Tenant);

        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        var correlationId = reservationId.ToString();

        await DispatchAsync(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));
        await DispatchAsync(new IdentityVerified(reservationId, IsValid: true));

        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();
        var saga = await repo.FindByCorrelationAsync(correlationId);
        saga.Should().NotBeNull("saga should exist after the two dispatched events");
        saga!.State.Should().Be(CheckInState.RoomAssigned,
            "[SagaTimeout] is declared on Handle(IdentityVerified) so the deadline only kicks in once we're past GuestVerified");

        await repo.SetTimeoutAsync(saga.Id, DateTimeOffset.UtcNow.AddSeconds(-10));

        var runners = scope.ServiceProvider.GetServices<ISagaTimeoutRunner>().ToList();
        runners.Should().NotBeEmpty(
            "SG must register an ISagaTimeoutRunner for sagas that declare [SagaTimeout] on at least one step");

        foreach (var r in runners)
            await r.RunDueTimeoutsAsync(DateTimeOffset.UtcNow, default);

        // The timeout outcome is persisted: read in a scope of its own, the saga is marked TimedOut
        // (Orchestrator.HandleTimeoutAsync → MarkTimedOutAsync).
        using var verifyScope = Services.CreateScope();
        var stored = verifyScope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();
        (await stored.GetStatusAsync(saga.Id)).Should().Be(SagaStatus.TimedOut,
            "the timeout runner should have invoked Orchestrator.HandleTimeoutAsync → MarkTimedOutAsync");
    }

    // Routes the message through the SG-generated IMessageHandler<T> chain, which is what a
    // transport consumer does after deserializing the payload. Resolving the handler directly
    // bypasses the transport hop entirely and exercises: handler → Orchestrator → repository.
    private async Task DispatchAsync<T>(T message) where T : notnull
    {
        using var scope = Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMessageHandler<T>>().ToList();
        handlers.Should().NotBeEmpty(
            $"SG must have registered at least one IMessageHandler<{typeof(T).Name}> for the saga — check AddPragmaticSagas wiring");
        foreach (var handler in handlers)
            await handler.HandleAsync(message, MessageContext.New());
    }

    private async Task AssertSagaStateAsync(string correlationId, CheckInState expected, string because)
    {
        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();
        var found = await repo.FindByCorrelationAsync(correlationId);
        found.Should().NotBeNull(because);
        found!.State.Should().Be(expected, because);
    }
}
