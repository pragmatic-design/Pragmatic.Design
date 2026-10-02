using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Events;
using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;
using Showcase;
using Showcase.Billing;
using Showcase.Billing.Actions;
using Showcase.Billing.Entities;
using Showcase.Billing.Enums;
using Showcase.Billing.Infrastructure.EventHandlers;
using Showcase.Billing.Events;
using Showcase.Booking;
using Showcase.Booking.Dtos;
using Showcase.Booking.Entities;
using Showcase.Booking.Enums;
using Showcase.Booking.Events;
using Showcase.Booking.Reservations.Mutations;
using Showcase.Booking.Infrastructure.Services;
using Showcase.Catalog.Entities;
using Showcase.Tests;
using Showcase.Tests.Integration;
using Showcase.Tests.Unit;
using Showcase.Tests.Unit.EventHandlers;
using Showcase.Tests.Unit.Services;
using Xunit;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
// The internal one: CreateDraftInvoice has no endpoint, so it is not Billing's surface and lives on
// IBillingInternalActions — which is what the handler under test consumes.
[assembly: GenerateMock<IBillingInternalActions>]
[assembly: GenerateMock<IBookingActions>]
[assembly: GenerateMock<IBookingReservationsActions>]
[assembly: GenerateMock<IClock>]
[assembly: GenerateMock<IDomainEventDispatcher>]
[assembly: GenerateMock<ILogger<LoggingReservationPricingService>>]
[assembly: GenerateMock<IReadRepository<RoomType>>]
[assembly: GenerateMock<IReservationPricingService>]
[assembly: GenerateMock<IVoidDomainActionInvoker<VoidInvoiceForReservationAction>>]

// And what this suite compares whole rather than member by member. Equals answers whether two
// values differ; a generated comparer answers WHERE — "Expected dto.LastName to be Doe, but found
// Roe" instead of a mapping test spelling out one assertion per property and saying nothing about
// the ones nobody thought to spell. Declared here for the same reason the mocks are: the list is
// the inventory.
[assembly: GenerateComparer<GuestDto>]
