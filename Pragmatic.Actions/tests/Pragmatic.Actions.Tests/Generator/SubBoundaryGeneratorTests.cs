using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for SubBoundary generation — sub-interfaces composed into root via property.
/// </summary>
public class SubBoundaryGeneratorTests : ActionsGeneratorTestBase
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
    public void SubBoundary_InferredFromNamespace_GeneratesSubInterface()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Sub-interface generated
        generated.Should().Contain("interface IBookingGuestsActions");
        generated.Should().Contain("CreateGuest(");

        // The control for PRAG0412: one level is the shape the warning exists to protect.
        HasDiagnostic(result, "PRAG0412").Should().BeFalse();
    }

    /// <summary>
    ///     The inference is the one point where a folder becomes public API without anyone writing
    ///     it, and PRAG0413 is the only signal of it. It is on by default and reported on the
    ///     operation: with no location it would sit at the top of the log instead of on the
    ///     operation that caused it.
    /// </summary>
    [Fact]
    public void SubBoundary_TheInference_IsReportedOnTheOperation()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0413").Single();

        diagnostic.Descriptor.IsEnabledByDefault.Should().BeTrue(
            "a signal nobody receives is the silence it was meant to break");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Info);
        diagnostic.GetMessage().Should().Contain("Guests");

        diagnostic.Location.Should().NotBe(Location.None);
        var span = diagnostic.Location.GetLineSpan();
        span.Path.Should().Be("TestSource.cs");
        source.Split('\n')[span.StartLinePosition.Line].Should().Contain("CreateGuestAction",
            "the line reported is the operation whose namespace produced the group");
    }

    /// <summary>
    ///     <c>Infrastructure/</c> holds what the module uses and does not publish. The segment stays in
    ///     the namespace and the inference discards it: an operation filed there lands on the root,
    ///     not in a group called <c>Infrastructure</c> — and not in <c>Infrastructure.Jobs</c> either,
    ///     which was two levels and therefore PRAG0412 as well.
    /// </summary>
    [Fact]
    public void SubBoundary_AnOperationUnderInfrastructure_StaysOnTheRoot()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Infrastructure.Jobs.Actions
            {
                [DomainAction(Internal = false)]
                public partial class DetectNoShowsAction : DomainAction<int>
                {
                    public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<int, IError>.Success(0));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        InterfaceBlock(generated!, "IBookingActions").Should().Contain("DetectNoShows(");
        generated.Should().NotContain("IBookingInfrastructure");
        generated.Should().NotContain("Infrastructure { get; }");
        HasDiagnostic(result, "PRAG0412").Should().BeFalse();
    }

    [Fact]
    public void SubBoundary_NestingDeeperThanTwoLevels_EmitsPrag0412()
    {
        // A sub-boundary path with more than 2 segments (Guests.Vip.Premium) should warn.
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Vip.Premium.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreatePremiumGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0412").Should().BeTrue(
            "a sub-boundary nested deeper than 2 levels should emit PRAG0412");
    }

    [Fact]
    public void SubBoundary_RootInterfaceHasProperties()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Root interface has property for sub-boundary
        generated.Should().Contain("IBookingGuestsActions Guests { get; }");
    }

    [Fact]
    public void SubBoundary_SubLocalActions_Generated()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Sub-boundary local implementation
        generated.Should().Contain("class BookingGuestsLocalActions : IBookingGuestsActions");
    }

    [Fact]
    public void SubBoundary_RootLocalActions_InjectsSubInterfaces()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Root local impl takes sub-interface in constructor
        generated.Should().Contain("BookingLocalActions");
        // The concrete twin, not the interface: both implement IBookingGuestsActions, and the
        // container would otherwise hand the guarded one to the internal root as well.
        generated.Should().Contain("BookingGuestsLocalActions guests");
    }

    [Fact]
    public void SubBoundary_DiRegistration_RegistersSubsAndRoot()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Sub-boundary registered
        // ⚠️ The public sub-interface answers with the guarded twin. Resolving it straight to the
        // unguarded implementation, which enters an internal call on every method, would mean a call
        // from another module through it never has the invoked operation's permission asked.
        generated.Should().Contain(
            "AddScoped<IBookingGuestsActions>(sp => sp.GetRequiredService<BookingGuestsLocalGuardedActions>())");
        generated.Should().Contain("AddScoped<BookingGuestsLocalActions>();",
            "and the unguarded twin is still there, for the root that answers the internal interface");

        // Root still registered
        generated.Should().Contain("AddScoped<BookingLocalActions>");
    }

    [Fact]
    public void SubBoundary_Mixed_BoundaryLevelAndSub()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;

                [DomainAction(Internal = false)]
                public partial class CheckAvailabilityAction : DomainAction<bool>
                {
                    public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<bool, IError>.Success(true));
                }
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Boundary-level method stays on root
        generated.Should().Contain("interface IBookingActions");
        generated.Should().Contain("CheckAvailability(");

        // Sub-boundary method on sub-interface
        generated.Should().Contain("interface IBookingGuestsActions");
        generated.Should().Contain("CreateGuest(");

        // Root has property for sub
        generated.Should().Contain("IBookingGuestsActions Guests { get; }");
    }

    [Fact]
    public void SubBoundary_NoSubFolders_FlatBackwardCompat()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;

                [DomainAction(Internal = false)]
                public partial class CreateReservationAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Flat interface — no sub-boundary properties
        generated.Should().Contain("interface IBookingActions");
        generated.Should().Contain("CreateReservation(");

        // Root impl as usual
        generated.Should().Contain("BookingLocalActions : IBookingInternalActions");

        // No sub-boundary property on root interface
        generated.Should().NotContain("{ get; }");

    }

    [Fact]
    public void SubBoundary_MultiLevel_ConcatenatesPath()
    {
        var source = CommonUsings + """

            namespace TestApp.Catalog
            {
                [Boundary]
                public partial class CatalogBoundary;
            }

            namespace TestApp.Catalog.Properties.Photos.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class UploadPhotoAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Multi-level concatenated: Properties.Photos → PropertiesPhotos
        generated.Should().Contain("interface ICatalogPropertiesPhotosActions");
        generated.Should().Contain("class CatalogPropertiesPhotosLocalActions");
        generated.Should().Contain("UploadPhoto(");

        // The control for PRAG0412: two levels are within the limit, the warning is for three.
        HasDiagnostic(result, "PRAG0412").Should().BeFalse();
    }

    [Fact]
    public void SubBoundary_InternalMembers_FlatOnRootInternal()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }

                [DomainAction(Internal = true)]
                public partial class SyncGuestAction : VoidDomainAction
                {
                    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(VoidResult<IError>.Success());
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Public sub-member in sub-interface
        generated.Should().Contain("interface IBookingGuestsActions");

        // Internal sub-member flat on root internal interface
        generated.Should().Contain("interface IBookingInternalActions");
        generated.Should().Contain("SyncGuest(");

        // SyncGuest should NOT be in the sub-interface
        // (split: public → sub, internal → root internal)
        var subInterfaceStart = generated!.IndexOf("interface IBookingGuestsActions");
        var subInterfaceEnd = generated.IndexOf("}", subInterfaceStart);
        var subInterfaceBlock = generated.Substring(subInterfaceStart, subInterfaceEnd - subInterfaceStart);
        subInterfaceBlock.Should().NotContain("SyncGuest");
    }

    [Fact]
    public void SubBoundary_MultipleSubs_AllGenerated()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }

            namespace TestApp.Booking.Reservations.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateReservationAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Both sub-interfaces generated
        generated.Should().Contain("interface IBookingGuestsActions");
        generated.Should().Contain("interface IBookingReservationsActions");

        // Both properties on root
        generated.Should().Contain("IBookingGuestsActions Guests { get; }");
        generated.Should().Contain("IBookingReservationsActions Reservations { get; }");

        // Both local implementations
        generated.Should().Contain("class BookingGuestsLocalActions");
        generated.Should().Contain("class BookingReservationsLocalActions");

        // Both registered in DI
        // ⚠️ The public sub-interface answers with the guarded twin. Resolving it straight to the
        // unguarded implementation, which enters an internal call on every method, would mean a call
        // from another module through it never has the invoked operation's permission asked.
        generated.Should().Contain(
            "AddScoped<IBookingGuestsActions>(sp => sp.GetRequiredService<BookingGuestsLocalGuardedActions>())");
        generated.Should().Contain("AddScoped<BookingGuestsLocalActions>();",
            "and the unguarded twin is still there, for the root that answers the internal interface");
        generated.Should().Contain(
            "AddScoped<IBookingReservationsActions>(sp => sp.GetRequiredService<BookingReservationsLocalGuardedActions>())");
    }

    [Fact]
    public void SubBoundary_BelongsTo_WithSubNamespace()
    {
        var source = CommonUsings + """

            namespace TestApp.Booking
            {
                [Boundary]
                public partial class BookingBoundary;
            }

            namespace TestApp.Billing
            {
                // Explicit BelongsTo — should go flat on root, not inferred as sub
                [DomainAction(Internal = false)]
                [BelongsTo<TestApp.Booking.BookingBoundary>]
                public partial class CrossBoundaryAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }

            namespace TestApp.Booking.Guests.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateGuestAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        var bookingGenerated = sources.FirstOrDefault(s =>
            s.Key.Contains("BookingBoundary") && s.Key.Contains("Definition"));
        bookingGenerated.Value.Should().NotBeNull();

        // BelongsTo action is flat on root (not in a sub-boundary)
        InterfaceBlock(bookingGenerated.Value, "IBookingActions").Should().Contain("CrossBoundary(");
        bookingGenerated.Value.Should().NotContain("IBookingBillingActions",
            "a namespace that has nothing to do with the boundary must not become one of its groups");

        // Guests sub-interface still generated
        bookingGenerated.Value.Should().Contain("interface IBookingGuestsActions");
        bookingGenerated.Value.Should().Contain("CreateGuest(");
    }

    /// <summary>
    ///     <c>[BelongsTo]</c> names the boundary and nothing else. It does not cancel the group the
    ///     namespace declares: if it did, an operation filed in <c>Amenities/</c> next to the mutations
    ///     that form the group would land on the root — silently, since the route is declared by hand
    ///     and no end-to-end case could see the difference.
    /// </summary>
    [Fact]
    public void SubBoundary_BelongsToInsideTheBoundaryNamespace_KeepsTheInferredGroup()
    {
        var source = CommonUsings + """

            namespace TestApp.Catalog
            {
                [Boundary]
                public partial class CatalogBoundary;
            }

            namespace TestApp.Catalog.Amenities.Mutations
            {
                [DomainAction(Internal = false)]
                public partial class CreateAmenityAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }

            namespace TestApp.Catalog.Amenities.Actions
            {
                [DomainAction(Internal = false)]
                [BelongsTo<TestApp.Catalog.CatalogBoundary>]
                public partial class CreateAmenityPairAction : VoidDomainAction
                {
                    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(VoidResult<IError>.Success());
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        InterfaceBlock(generated!, "ICatalogAmenitiesActions").Should().Contain("CreateAmenityPair(",
            "the attribute chose the boundary; the group still comes from the namespace");
        InterfaceBlock(generated!, "ICatalogActions").Should().NotContain("CreateAmenityPair(");
    }

    /// <summary>The body of one generated interface, so an assertion cannot match a method on the wrong one.</summary>
    /// <remarks>
    ///     The interface closes at the first brace on a line of its own: a property's <c>{ get; }</c>
    ///     sits inside the body, and stopping at the first <c>}</c> cut the root interface off at its
    ///     first sub-boundary property.
    /// </remarks>
    private static string InterfaceBlock(string generated, string interfaceName)
    {
        var start = generated.IndexOf($"interface {interfaceName}", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the generated boundary declares {interfaceName}");
        var end = generated.IndexOf("\n}", start, StringComparison.Ordinal);
        return generated.Substring(start, end - start);
    }
}
