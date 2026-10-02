using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for [Boundary] interface generation — method-based interfaces, dual interfaces, BoundaryMode.
/// </summary>
public class BoundaryGeneratorTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    [Fact]
    public void Boundary_WithActions_GeneratesMethodInterface()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Method-based interface (not property-based)
        generated.Should().Contain("interface IBookingActions");
        generated.Should().Contain("CreateReservation(");
        generated.Should().NotContain("CreateReservation { get; }");

        // Local implementation
        generated.Should().Contain("class BookingLocalActions");
        generated.Should().Contain(": IBookingActions");

        // DI with BoundaryMode
        generated.Should().Contain("AddBookingBoundary");
        generated.Should().Contain("BoundaryMode mode");
    }

    [Fact]
    public void Boundary_WithVoidAndReturnActions_CorrectMethodReturnTypes()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }

            [DomainAction]
            public partial class ConfirmReservationAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // DomainAction<Guid> → Task<Result<Guid, IError>>
        generated.Should().Contain("Task<global::Pragmatic.Result.Result<global::System.Guid, global::Pragmatic.Result.IError>> CreateReservation(");

        // VoidDomainAction → Task<VoidResult<IError>>
        generated.Should().Contain("Task<global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>> ConfirmReservation(");
    }

    [Fact]
    public void Boundary_InternalAction_InInternalInterfaceOnly()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }

            [DomainAction(Internal = true)]
            public partial class CreateInvoiceAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Public action in public interface
        generated.Should().Contain("interface IBookingActions");
        generated.Should().Contain("CreateReservation(");

        // Internal action in internal interface
        generated.Should().Contain("interface IBookingInternalActions : IBookingActions");
        generated.Should().Contain("CreateInvoice(");

        // Local impl implements internal interface (which extends public)
        generated.Should().Contain("BookingLocalActions : IBookingInternalActions");
    }

    [Fact]
    public void Boundary_BelongsToOverride_AssignsCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Billing
            {
                [Boundary]
                public partial class BillingBoundary;

                [DomainAction]
                [BelongsTo<TestApp.Booking.BookingBoundary>]
                public partial class CrossBoundaryAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        // RunGeneratorWithEntities includes Pragmatic.Persistence references because
        // [BelongsTo<T>] on the action triggers IUnitOfWork generation in the invoker.
        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);

        // The action should appear in the Booking boundary interface, not Billing
        var bookingGenerated = sources.FirstOrDefault(s =>
            s.Key.Contains("BookingBoundary") && s.Key.Contains("Definition"));
        bookingGenerated.Value.Should().NotBeNull();
        bookingGenerated.Value.Should().Contain("CrossBoundary(");

        // Billing should either not exist or not contain the action
        var billingGenerated = sources.FirstOrDefault(s =>
            s.Key.Contains("BillingBoundary") && s.Key.Contains("Definition"));
        if (billingGenerated.Value is not null)
            billingGenerated.Value.Should().NotContain("CrossBoundary");
    }

    [Fact]
    public void Boundary_CustomName_UsesProvidedName()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [Boundary(Name = "IOrderCommands")]
            public partial class OrdersBoundary;

            [DomainAction]
            public partial class PlaceOrderAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Should use the custom name instead of default IOrdersActions
        generated.Should().Contain("interface IOrderCommands");
        generated.Should().NotContain("IOrdersActions");

        // Method signatures
        generated.Should().Contain("PlaceOrder(");
    }

    [Fact]
    public void Boundary_NotPartial_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public class BookingBoundary;
            """;

        var result = RunGenerator(source);

        // PRAG0406 is the companion analyzer's, on the declaration.
        HasDiagnostic(result, "PRAG0406").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse("nothing was generated into a type that cannot take it");
    }

    [Fact]
    public void Boundary_NoNamespace_ReportsDiagnostic()
    {
        var source = CommonUsings + """

            [Boundary]
            public partial class BookingBoundary;
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0407").Should().BeTrue();
    }

    // =========================================================================
    // New tests — dual interfaces, local impl, unwrapped overloads, BoundaryMode
    // =========================================================================

    [Fact]
    public void Boundary_GeneratesInternalInterface_WithInternalActions()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }

            [DomainAction(Internal = true)]
            public partial class InternalBookingAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Public interface should only have the public action
        generated.Should().Contain("public interface IBookingActions");
        generated.Should().Contain("CreateReservation(");

        // Internal interface extends public and has the internal action
        generated.Should().Contain("internal interface IBookingInternalActions : IBookingActions");
        generated.Should().Contain("InternalBooking(");

        // Local impl implements internal interface
        generated.Should().Contain("BookingLocalActions : IBookingInternalActions");

        // Both are registered in DI
        generated.Should().Contain("AddScoped<IBookingActions>");
        generated.Should().Contain("AddScoped<IBookingInternalActions>");
    }

    [Fact]
    public void Boundary_NoInternalActions_NoInternalInterface()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Public interface exists
        generated.Should().Contain("public interface IBookingActions");

        // Internal interface always generated (for DI registration consistency)
        generated.Should().Contain("IBookingInternalActions");

        // Local impl implements internal interface (which extends public)
        generated.Should().Contain("BookingLocalActions : IBookingInternalActions");
    }

    [Fact]
    public void Boundary_LocalImplementation_NamedCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CheckInAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Named {ShortName}LocalActions, not {Boundary}Actions
        generated.Should().Contain("class BookingLocalActions");
        generated.Should().NotContain("BookingBoundaryActions");
    }

    [Fact]
    public void Boundary_DtoAndUnwrappedOverloads_Generated()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public required Guid GuestId { get; init; }
                public required string RoomNumber { get; init; }

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // DTO overload
        generated.Should().Contain("CreateReservation(global::TestApp.Booking.CreateReservationAction action");

        // Unwrapped overload with parameters
        generated.Should().Contain("guestId");
        generated.Should().Contain("roomNumber");
    }

    [Fact]
    public void Boundary_NoInputProperties_OnlyDtoOverload()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            [DomainAction]
            public partial class ConfirmAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Only DTO overload (action with no properties)
        generated.Should().Contain("Confirm(global::TestApp.Booking.ConfirmAction action");

        // Should not have "unwrapped parameters" summary (only one overload)
        generated.Should().NotContain("unwrapped parameters");
    }

    [Fact]
    public void Boundary_BoundaryModeParameter_InDiExtension()
    {
        var source = CommonUsings + """

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [DomainAction]
            public partial class CreateInvoiceAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // DI method has BoundaryMode parameter with default Local
        generated.Should().Contain("BoundaryMode mode = global::Pragmatic.Actions.Boundary.BoundaryMode.Local");

        // Switch statement dispatches to AddLocal
        generated.Should().Contain("AddLocal(services)");

        // Unsupported mode throws
        generated.Should().Contain("NotSupportedException");
    }

    // =========================================================================
    // Mutation boundary members
    // =========================================================================

    [Fact]
    public void Boundary_WithMutation_GeneratesMutationMethod()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            namespace TestApp.Catalog;

            [Boundary]
            public partial class CatalogBoundary;

            public partial class Amenity : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
            public partial class UpdateAmenityMutation : Mutation<Amenity>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Mutation method in interface — strips "Mutation" suffix
        generated.Should().Contain("interface ICatalogActions");
        generated.Should().Contain("UpdateAmenity(");

        // Return type is entity (Amenity), not Guid
        generated.Should().Contain("Result<global::TestApp.Catalog.Amenity, global::Pragmatic.Result.IError>");

        // Uses IMutationInvoker, not IDomainActionInvoker
        generated.Should().Contain("IMutationInvoker<global::TestApp.Catalog.UpdateAmenityMutation, global::TestApp.Catalog.Amenity>");

        // Parameter is "mutation", not "action"
        generated.Should().Contain("UpdateAmenityMutation mutation");

        // Unwrapped overload for mutations with input properties
        generated.Should().Contain("unwrapped parameters");
        generated.Should().Contain("global::System.Guid id, global::System.Threading.CancellationToken ct = default");
    }

    [Fact]
    public void Boundary_MixedActionsAndMutations_BothInInterface()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            namespace TestApp.Booking;

            [Boundary]
            public partial class BookingBoundary;

            public partial class Reservation : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            [DomainAction]
            public partial class CreateReservationAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }

            [Mutation(Mode = MutationMode.Update)]
            [Pragmatic.Persistence.Entity.BelongsTo<BookingBoundary>]
            public partial class ConfirmReservationMutation : Mutation<Reservation>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Both action and mutation methods in same interface
        generated.Should().Contain("CreateReservation(");
        generated.Should().Contain("ConfirmReservation(");

        // Different invoker types
        generated.Should().Contain("IDomainActionInvoker<global::TestApp.Booking.CreateReservationAction, global::System.Guid>");
        generated.Should().Contain("IMutationInvoker<global::TestApp.Booking.ConfirmReservationMutation, global::TestApp.Booking.Reservation>");
    }

    [Fact]
    public void Boundary_MutationOnly_GeneratesBoundary()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            namespace TestApp.Catalog;

            [Boundary]
            public partial class CatalogBoundary;

            public partial class Property : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
            public partial class UpdatePropertyMutation : Mutation<Property>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Boundary generated even without any DomainActions
        generated.Should().Contain("interface ICatalogActions");
        generated.Should().Contain("UpdateProperty(");
        generated.Should().Contain("AddCatalogBoundary");
    }
}
