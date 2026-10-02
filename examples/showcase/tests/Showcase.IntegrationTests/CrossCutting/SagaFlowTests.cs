using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Saga;
using Pragmatic.MultiTenancy;
using Showcase.Booking.Entities;
using Showcase.Booking.Reservations.Sagas;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests the CheckInSaga orchestrated flow via the E2E pipeline.
///     The saga is triggered by domain events (GuestArrived → IdentityVerified → RoomReady).
///     BookingBoundary is [EnableSagaPersistence], so the saga is EF Core-backed
///     (EfCoreSagaRepository over the __SagaInstances/__SagaSteps tables in BookingDbContext):
///     state survives across scopes and process restarts, verified here against the real database.
/// </summary>
public class SagaFlowTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
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

    // =========================================================================
    // Saga: CheckInSaga logic — GuestArrived triggers check-in process
    // =========================================================================

    [Fact]
    public void SagaFlow_GuestArrival_TriggersCheckInProcess()
    {
        // The saga orchestrator is SG-generated and internal, with InMemorySagaRepository
        // registered as scoped (per-request). We test saga logic directly since
        // cross-request saga state doesn't persist with in-memory scoped registration.

        var saga = new CheckInSaga();
        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        // Step 1: GuestArrived → SagaStart → state transitions to GuestVerified
        var verifyAction = saga.Handle(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));

        saga.State.Should().Be(CheckInState.GuestVerified,
            "[SagaStart] GuestArrived should transition saga to GuestVerified state");
        saga.ReservationId.Should().Be(reservationId,
            "Saga should capture the ReservationId from the triggering event");
        verifyAction.Should().NotBeNull(
            "GuestArrived handler should dispatch VerifyGuestIdentityAction");
        verifyAction.ReservationId.Should().Be(reservationId);
        verifyAction.GuestId.Should().Be(guestId);
    }

    // =========================================================================
    // Saga: Full orchestration flow — arrival to completion
    // =========================================================================

    [Fact]
    public void SagaFlow_FullOrchestration_ArrivalToCompletion()
    {
        var saga = new CheckInSaga();
        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        // Step 1: GuestArrived → GuestVerified
        var verifyAction = saga.Handle(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));
        saga.State.Should().Be(CheckInState.GuestVerified);
        verifyAction.Should().NotBeNull();

        // Step 2: IdentityVerified (valid) → dispatch AssignRoomAction
        var assignAction = saga.Handle(new IdentityVerified(reservationId, IsValid: true));
        assignAction.Should().NotBeNull(
            "[InState(GuestVerified)] valid identity should dispatch AssignRoomAction");
        assignAction.ReservationId.Should().Be(reservationId);

        // Orchestrator transitions to RoomAssigned
        saga.State = CheckInState.RoomAssigned;

        // Step 3: RoomReady → dispatch CompleteCheckInAction
        var completeAction = saga.Handle(new RoomReady(reservationId, "301A"));
        saga.State.Should().Be(CheckInState.Completed,
            "Saga should be in Completed state after full flow");
        saga.AssignedRoom.Should().Be("301A",
            "Saga should store the assigned room number");
        saga.CompletedAt.Should().NotBeNull(
            "CompletedAt should be set when saga reaches terminal state");
        completeAction.Should().NotBeNull();
        completeAction.RoomNumber.Should().Be("301A");
    }

    // =========================================================================
    // Saga: Compensation — IdentityVerified(IsValid=false) cancels saga
    // =========================================================================

    // The handler rejects an invalid identity by throwing SagaRejectedException (the business-rejection
    // convention). A direct call surfaces the throw; the orchestrator turns it into compensation.
    [Fact]
    public void CheckInSaga_InvalidIdentity_ThrowsSagaRejectedException()
    {
        var saga = new CheckInSaga();
        var act = () => saga.Handle(new IdentityVerified(Guid.NewGuid(), IsValid: false));
        act.Should().Throw<SagaRejectedException>();
    }

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

        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var instance = await db.Set<SagaInstance>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == saga.Id);
        instance.Should().NotBeNull("the saga instance must be persisted");
        instance!.Status.Should().Be(SagaStatus.Compensated,
            "a rejected step must run compensation and leave the saga Compensated, not Active");
    }

    // =========================================================================
    // Saga: Repository registered in DI (verifies SG registration)
    // =========================================================================

    [Fact]
    public void SagaRepository_RegisteredInDI_CanResolve()
    {
        // SG-generated PragmaticSagaRegistration.AddPragmaticSagas is auto-invoked by
        // the host via MetadataCategory.Sagas aggregation; the repository must resolve.
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();

        repository.Should().NotBeNull();
    }

    [Fact]
    public async Task SagaRepository_SaveAndFind_RoundTrips()
    {
        using var tenant = TenantScope.BeginScope(Tenant);
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();

        var saga = new CheckInSaga
        {
            Id = Guid.NewGuid(),
            CorrelationId = $"test-{Guid.NewGuid():N}",
            State = CheckInState.GuestVerified,
            ReservationId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveAsync(saga);
        var found = await repository.FindByCorrelationAsync(saga.CorrelationId);

        found.Should().NotBeNull(
            "Saga saved to repository should be retrievable by CorrelationId");
        found!.State.Should().Be(CheckInState.GuestVerified);
        found.ReservationId.Should().Be(saga.ReservationId);
    }

    [Fact]
    public void SagaRepository_IsEfCoreBacked_NotInMemory()
    {
        // BookingBoundary is [EnableSagaPersistence]: the SG wires EfCoreSagaRepository (durable) rather
        // than the in-memory default, which is what lets saga state survive a process restart.
        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();

        repo.Should().BeOfType<EfCoreSagaRepository<CheckInSaga, CheckInState>>();
    }

    // =========================================================================
    // E2E: publish events via IMessageBus → SG-generated handlers route to
    // Orchestrator → saga state persists across per-request scopes via the
    // Singleton InMemorySagaRepository. This is the end-to-end chain users rely on.
    // =========================================================================

    [Fact]
    public async Task SagaFlow_EndToEnd_PublishEventsViaBus_AdvancesSaga()
    {
        using var tenant = TenantScope.BeginScope(Tenant);

        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        var correlationId = reservationId.ToString();

        // Publish via DispatchAsync so the message is routed locally through the SG-generated
        // IMessageHandler<T> without going out to the Channel transport. This exercises the full
        // chain (handler → Orchestrator → repository) synchronously, which is exactly what a
        // consumer does after pulling the message off its transport.
        await DispatchAsync(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));

        await AssertSagaStateAsync(correlationId, CheckInState.GuestVerified,
            "SG-generated EventHandler_GuestArrived must route GuestArrived through the Orchestrator and persist the saga");

        await DispatchAsync(new IdentityVerified(reservationId, IsValid: true));

        await AssertSagaStateAsync(correlationId, CheckInState.RoomAssigned,
            "Orchestrator must route IdentityVerified through the GuestVerified transition");

        await DispatchAsync(new RoomReady(reservationId, "507"));

        using var finalScope = Services.CreateScope();
        var finalRepo = finalScope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();
        var finalSaga = await finalRepo.FindByCorrelationAsync(correlationId);
        finalSaga.Should().NotBeNull();
        finalSaga!.State.Should().Be(CheckInState.Completed,
            "Saga must reach terminal state after the full three-step orchestration");
        finalSaga.AssignedRoom.Should().Be("507");
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

        // EF-backed saga: the timeout outcome is persisted. Verify the __SagaInstances row was marked
        // TimedOut (Orchestrator.HandleTimeoutAsync → MarkTimedOutAsync) directly against the database.
        using var verifyScope = Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var instance = await db.Set<SagaInstance>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == saga.Id);
        instance.Should().NotBeNull("the saga must be persisted in __SagaInstances");
        instance!.Status.Should().Be(SagaStatus.TimedOut,
            "the timeout runner should have invoked Orchestrator.HandleTimeoutAsync → MarkTimedOutAsync");
    }

    [Fact]
    public async Task SagaFlow_EndToEnd_UnrelatedEvent_DoesNotStartSaga()
    {
        // IdentityVerified is NOT the saga-start event. Dispatching it without a prior
        // GuestArrived must not create a saga instance — the orchestrator returns early
        // when instance is null and the event is not the SagaStart type.
        var correlationId = Guid.NewGuid().ToString();

        await DispatchAsync(new IdentityVerified(Guid.Parse(correlationId), IsValid: true));

        using var finalScope = Services.CreateScope();
        var repo = finalScope.ServiceProvider.GetRequiredService<ISagaRepository<CheckInSaga>>();
        var found = await repo.FindByCorrelationAsync(correlationId);
        found.Should().BeNull("Non-start event with no prior saga instance must not persist anything");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //   Helpers
    // ─────────────────────────────────────────────────────────────────────────

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

    // =========================================================================
    // Saga: HTTP-driven lifecycle leads to check-in (which would trigger saga)
    // Verifies the HTTP endpoints that drive the reservation state machine
    // to the CheckedIn state — the point where GuestCheckedIn event fires.
    // =========================================================================

    [Fact]
    public async Task SagaFlow_HttpLifecycle_ReservationReachesCheckedInState()
    {
        // Use premium-hotel tenant for early check-in capability
        using var premiumClient = CreateClientAs("saga-user", "Saga Test User", "premium-hotel");

        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(premiumClient);
        var reservationId = await CreateReservationAsync(premiumClient, guestId, propertyId, roomTypeId);

        // Pending → Confirmed
        var confirmResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirmed → PaymentReceived
        var paymentResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/payment-received", new { });
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify intermediate state — through the same client that wrote it. Reading through the base
        // Client is reading from another tenant, and it answered only because the read-access Property
        // was unfiltered there.
        var afterPayment = await GetWithClientAsync<JsonElement>(
            premiumClient, $"/api/reservations/{reservationId}");
        afterPayment.GetProperty("status").GetString().Should().Be("PaymentReceived",
            "Reservation should be in PaymentReceived state before check-in");

        // PaymentReceived → CheckedIn (triggers GuestCheckedIn domain event)
        var checkInResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/check-in", new { });
        checkInResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify reservation is in CheckedIn state — the trigger point for saga events
        var reservation = await GetWithClientAsync<JsonElement>(
            premiumClient, $"/api/reservations/{reservationId}");
        reservation.GetProperty("status").GetString().Should().Be("CheckedIn",
            "Reservation should reach CheckedIn state, which fires GuestCheckedIn event");
    }

    // =========================================================================
    // Saga: State transition validation — wrong event in wrong state
    // =========================================================================

    [Fact]
    public void SagaFlow_InvalidStateTransition_DoesNotProgress()
    {
        var saga = new CheckInSaga();

        // Attempt IdentityVerified without going through GuestArrived first
        // (saga is in Pending state, not GuestVerified)
        var result = saga.Handle(new IdentityVerified(Guid.NewGuid(), IsValid: true));

        // The saga handler doesn't guard state; the orchestrator does.
        // But the saga itself should not crash — it returns an action regardless.
        // The orchestrator's routing logic prevents calling this handler in wrong state.
        result.Should().NotBeNull("Handler returns action; orchestrator filters by state");

        // RoomReady on a fresh saga (not in RoomAssigned state)
        var saga2 = new CheckInSaga();
        var roomAction = saga2.Handle(new RoomReady(Guid.NewGuid(), "101"));

        // Handler sets Completed state directly — the orchestrator guards this
        saga2.State.Should().Be(CheckInState.Completed,
            "Handler unconditionally sets state; orchestrator is the real guard");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(
        HttpClient client)
    {
        var guestResponse = await client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "Saga",
            lastName = "Test",
            email = $"saga.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestResponse.EnsureSuccessStatusCode();
        var guest = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guest.GetProperty("id").GetGuid();

        var propResponse = await client.PostAsJsonAsync("/api/properties", new
        {
            code = $"SG-{Guid.NewGuid():N}"[..12],
            name = $"SagaProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 4
        }, JsonOptions);
        propResponse.EnsureSuccessStatusCode();
        var prop = await propResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = prop.GetProperty("id").GetGuid();

        var rtResponse = await client.PostAsJsonAsync("/api/room-types", new
        {
            propertyId,
            name = "Saga Room",
            code = "SGR",
            baseRate = 200m,
            totalRooms = 10
        }, JsonOptions);
        rtResponse.EnsureSuccessStatusCode();
        var rt = await rtResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var roomTypeId = rt.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }

    private static async Task<Guid> CreateReservationAsync(
        HttpClient client, Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var response = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2
            }
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
