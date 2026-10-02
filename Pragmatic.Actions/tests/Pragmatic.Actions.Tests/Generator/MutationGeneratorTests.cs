using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for the [Mutation] pipeline generation:
///     MutationInvoker, ApplyToEntity, SetDependencies, Registration, and diagnostics.
/// </summary>
public class MutationGeneratorTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    private const string ReservationEntity = """

        namespace TestApp;

        public class Reservation : IEntity
        {
            public Guid PersistenceId { get; set; }

            // private set, as every real entity declares them: Set{Name} is generated only for a
            // setter the outside cannot reach, and this fixture hand-writes what the entity generator
            // would emit. With public setters it modelled a shape that never occurs, and the auto-map
            // called wrappers that in a real project would not exist.
            public Guid GuestId { get; private set; }
            public Guid PropertyId { get; private set; }
            public DateOnly CheckIn { get; private set; }
            public DateOnly CheckOut { get; private set; }
            public string Status { get; private set; } = "Pending";
            public string? Notes { get; private set; }

            internal void SetGuestId(Guid value) => GuestId = value;
            internal void SetPropertyId(Guid value) => PropertyId = value;
            internal void SetCheckIn(DateOnly value) => CheckIn = value;
            internal void SetCheckOut(DateOnly value) => CheckOut = value;
            internal void SetStatus(string value) => Status = value;
            internal void SetNotes(string? value) => Notes = value;
        }
        """;

    [Fact]
    public void Mutation_WithRequirePolicy_IsEnforced_RegisteredInPolicyRegistry()
    {
        // [RequirePolicy<T>] on a mutation is supported —
        // MutationInvoker.CheckPolicyAndResourceAsync evaluates it and its factory lands in the
        // generated policy registry. ReservationEntity uses a file-scoped namespace, so append types directly (no new
        // using/namespace) and reference policy types fully-qualified.
        var source = CommonUsings + ReservationEntity + """

            public sealed class CanManageReservationsPolicy : global::Pragmatic.Authorization.Policy.ResourcePolicy
            {
                public override bool Evaluate(global::Pragmatic.Identity.ICurrentUser user) => user.IsAuthenticated;
            }

            [Mutation]
            [global::Pragmatic.Authorization.Policy.RequirePolicy<CanManageReservationsPolicy>]
            public partial class CreateReservationGuarded : Mutation<Reservation>
            {
                public Guid GuestId { get; init; }
                public Guid PropertyId { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0416").Should().BeFalse(
            "[RequirePolicy] on a mutation is now enforced at runtime and must not be rejected");
        HasCompilationErrors(result).Should().BeFalse();

        var generated = string.Concat(GetGeneratedSourcesAsDictionary(result).Values);
        generated.Should().Contain("GeneratedPolicyRegistry");
        generated.Should().Contain("CreateReservationGuarded");
        generated.Should().Contain("CanManageReservationsPolicy");
    }

    [Fact]
    public void Mutation_ReturningTheLogicalKeyOfAnEntityWithoutOne_IsPrag0403()
    {
        // Reservation declares no [LogicKey]: there is nothing to return, and a record with no parts
        // would be a key that identifies nothing.
        var source = CommonUsings + ReservationEntity + """

            [Mutation(ReturnType = global::Pragmatic.Actions.Mutation.MutationReturnType.LogicalKey)]
            public partial class CreateReservationWithKey : Mutation<Reservation>
            {
                public Guid GuestId { get; init; }
                public Guid PropertyId { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        var diagnostic = GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0403").Should().ContainSingle().Which;
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("declares no [LogicKey]");
        HasDiagnostic(result, "PRAG0417").Should().BeFalse("PRAG0417 is gone: every ReturnType is wired");
    }

    /// <summary>The control: an entity that declares its key returns it, and nothing is reported.</summary>
    [Fact]
    public void Mutation_ReturningTheLogicalKeyOfAnEntityWithOne_GeneratesTheRecordTheBoundaryReturns()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class Voucher : IEntity
            {
                public Guid PersistenceId { get; set; }

                [LogicKey]
                public string Code { get; private set; } = "";

                public int Amount { get; private set; }

                internal void SetCode(string value) => Code = value;
                internal void SetAmount(int value) => Amount = value;
            }

            [Mutation(ReturnType = global::Pragmatic.Actions.Mutation.MutationReturnType.LogicalKey)]
            public partial class CreateVoucher : Mutation<Voucher>
            {
                public required string Code { get; init; }
                public int Amount { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0403").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var record = GetGeneratedSource(result, "CreateVoucher.LogicalKey");
        record.Should().NotBeNull("a LogicalKey mutation gets the record it returns");
        record.Should().Contain("public required string Code { get; init; }");
        record.Should().NotContain("Amount", "only the parts of the [LogicKey] are the key");
        record.Should().Contain("public static LogicalKey From(global::TestApp.Voucher entity)");
    }

    [Fact]
    public void CreateMutation_AutoMap_GeneratesInvokerAndAutoMap()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation]
            public partial class CreateReservation : Mutation<Reservation>
            {
                public Guid GuestId { get; init; }
                public Guid PropertyId { get; init; }
                public DateOnly CheckIn { get; init; }
                public DateOnly CheckOut { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        // Invoker generated with Create mode
        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("class Invoker");
        invoker.Should().Contain("MutationInvoker<");
        invoker.Should().Contain("MutationMode.Create");

        // Auto-map generated (no manual ApplyAsync override)
        var autoMap = GetGeneratedSource(result, "ApplyToEntity");
        autoMap.Should().NotBeNull();
        autoMap.Should().Contain("ApplyToEntity");
        autoMap.Should().Contain("entity.SetGuestId(this.GuestId)");
        autoMap.Should().Contain("entity.SetPropertyId(this.PropertyId)");
        autoMap.Should().Contain("entity.SetCheckIn(this.CheckIn)");
        autoMap.Should().Contain("entity.SetCheckOut(this.CheckOut)");
    }

    [Fact]
    public void UpdateMutation_WithManualApply_GeneratesInvokerOnly()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation(Mode = MutationMode.Update)]
            public partial class CheckInGuest : Mutation<Reservation>
            {
                public required Guid Id { get; init; }

                public override Task<Result<Reservation, IError>> ApplyAsync(
                    Reservation entity, CancellationToken ct = default)
                {
                    entity.SetStatus("CheckedIn");
                    return Task.FromResult<Result<Reservation, IError>>(entity);
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        // Invoker generated with Update mode
        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("class Invoker");
        invoker.Should().Contain("MutationMode.Update");

        // Auto-map NOT generated (manual ApplyAsync override present)
        var autoMap = GetGeneratedSource(result, "ApplyToEntity");
        autoMap.Should().BeNull("auto-map should not be generated when ApplyAsync is overridden");
    }

    [Fact]
    public void Mutation_WithDependencies_GeneratesSetDependencies()
    {
        var source = CommonUsings + ReservationEntity + """

            public interface IReservationRepository { }

            [Mutation]
            public partial class CreateReservation : Mutation<Reservation>
            {
                private IReservationRepository _reservationRepository;

                public Guid GuestId { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        // SetDependencies generated
        var setDeps = GetGeneratedSource(result, "SetDependencies");
        setDeps.Should().NotBeNull();
        setDeps.Should().Contain("SetDependencies");
        setDeps.Should().Contain("IReservationRepository");
        setDeps.Should().Contain("_reservationRepository = reservationRepository;");

        // Invoker calls SetDependencies
        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("mutation.SetDependencies(");
    }

    [Fact]
    public void Mutation_NullableProperty_GeneratesConditionalMapping()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateReservation : Mutation<Reservation>
            {
                public required Guid Id { get; init; }
                public string? Notes { get; init; }
                public DateOnly CheckIn { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var autoMap = GetGeneratedSource(result, "ApplyToEntity");
        autoMap.Should().NotBeNull();

        // Nullable reference type: pattern matching unwrap
        autoMap.Should().Contain("this.Notes is { } notes");
        autoMap.Should().Contain("entity.SetNotes(notes)");

        // Non-nullable value type: direct mapping (no null check)
        autoMap.Should().Contain("entity.SetCheckIn(this.CheckIn)");
        autoMap.Should().NotContain("this.CheckIn is not null");
    }

    /// <remarks>
    ///     The entity declares the navigations the paths name. It used not to — <c>Reservation</c> has neither — and
    ///     the test passed because nothing checked an <c>[EagerLoad]</c> path: it asserted an <c>Include</c> EF Core
    ///     would refuse at the first request. Such a path is PRAG0453 and is not emitted.
    /// </remarks>
    [Fact]
    public void Mutation_WithIncludes_GeneratesQueryWithIncludes()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class Product : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            public class OrderLine : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Product Product { get; private set; } = null!;
            }

            public class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public System.Collections.Generic.List<OrderLine> Lines { get; private set; } = [];
            }

            [Mutation(Mode = MutationMode.Update)]
            [EagerLoad("Lines")]
            [EagerLoad("Lines.Product")]
            public partial class UpdateOrderLines : Mutation<Order>
            {
                public required Guid Id { get; init; }

                public override Task<Result<Order, IError>> ApplyAsync(
                    Order entity, CancellationToken ct = default)
                {
                    return Task.FromResult<Result<Order, IError>>(entity);
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        // Note: generated code references EntityFrameworkQueryableExtensions
        // which may not be in the test references — only verify content
        HasDiagnostic(result, "PRAG0453").Should().BeFalse("both paths name navigations of the entity");
        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("Include(query, \"Lines\")");
        invoker.Should().Contain("Include(query, \"Lines.Product\")");
        invoker.Should().Contain("FirstOrDefaultAsync");
    }

    [Fact]
    public void MultipleMutations_GenerateAggregateRegistration()
    {
        var source = CommonUsings + ReservationEntity + """

            public class Property : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
                internal void SetName(string value) => Name = value;
            }

            [Mutation]
            public partial class CreateReservation : Mutation<Reservation>
            {
                public Guid GuestId { get; init; }
            }

            [Mutation]
            public partial class UpdateProperty : Mutation<Property>
            {
                public required Guid Id { get; init; }
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var registration = GetGeneratedSource(result, "Mutations.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("CreateReservation.Invoker");
        registration.Should().Contain("UpdateProperty.Invoker");
        registration.Should().Contain("AddScoped");
    }

    /// <summary>
    ///     Skipped without a generator diagnostic: PRAG0400 is the companion analyzer's, on the declaration.
    /// </summary>
    [Fact]
    public void NonPartialMutation_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation]
            public class CreateReservation : Mutation<Reservation>
            {
                public Guid GuestId { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0400").Should().BeFalse();
        GetGeneratedSource(result, "CreateReservation").Should().BeNull("a non-partial type cannot take a generated part");
    }

    [Fact]
    public void MutationWithoutBaseType_EmitsPRAG0409Diagnostic()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation]
            public partial class CreateReservation
            {
                public Guid GuestId { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0409").Should().BeTrue();
    }

    [Fact]
    public void MutationWithUnresolvableMode_EmitsPRAG0410Diagnostic()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation]
            public partial class CheckInGuest : Mutation<Reservation>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0410").Should().BeTrue();
    }

    [Fact]
    public void UpdateMutation_WithIdProperty_LoadsByIdInInvoker()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateReservation : Mutation<Reservation>
            {
                public required Guid Id { get; init; }
                public DateOnly CheckIn { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("GetByIdAsync(mutation.Id");
        invoker.Should().Contain("mutation.Id.ToString()");
    }

    /// <summary>
    ///     PRAG0414 — a mutation property the entity has no way to receive.
    /// </summary>
    /// <remarks>
    ///     The write path assigns through <c>Set{Name}</c>, so a property matching neither a declared
    ///     member nor one a generator will add is carried on the wire, bound from the body, and written
    ///     nowhere. Two tests named this id to assert its <em>absence</em>; nothing asserted it is ever
    ///     emitted, which leaves it indistinguishable from a diagnostic that was switched off.
    /// </remarks>
    [Fact]
    public void Mutation_PropertyWithNoSetterOnTheEntity_ReportsPrag0414()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateReservationNickname : Mutation<Reservation>
            {
                public Guid Id { get; init; }

                // Reservation has no Nickname and no SetNickname: nothing will receive this.
                public string Nickname { get; init; } = "";
            }
            """;

        var result = RunGeneratorWithEntities(source);

        GetGeneratorDiagnostics(result)
            .Where(d => d.Id == "PRAG0414")
            .Should().ContainSingle(d => d.GetMessage().Contains("Nickname"),
                "the property is bound from the body and written nowhere, and only the build can say so");
    }

    /// <summary>The control: a property the entity can receive is not reported.</summary>
    [Fact]
    public void Mutation_PropertyWithASetterOnTheEntity_IsNotReported()
    {
        var source = CommonUsings + ReservationEntity + """

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateReservationNotes : Mutation<Reservation>
            {
                public Guid Id { get; init; }
                public string? Notes { get; init; }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        GetGeneratorDiagnostics(result).Should().NotContain(d => d.Id == "PRAG0414");
    }

    /// <summary>
    ///     A mutation property named after a foreign key that <c>[Relation.*]</c> generates.
    /// </summary>
    /// <remarks>
    ///     The property matcher read only declared members, so it reported PRAG0414 "no matching
    ///     setter" while <c>SetSupplierId</c> was being generated three files away — and the value
    ///     was never applied. A consumer worked around it by declaring every foreign key by hand.
    /// </remarks>
    [Fact]
    public void Mutation_PropertyNamedAfterRelationForeignKey_IsMapped()
    {
        var source = CommonUsings + """

            namespace TestApp;

            [Entity]
            public partial class Supplier : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToOne<Supplier>]
            public partial class Assignment : IEntity
            {
                public Guid PersistenceId { get; set; }
                public decimal Amount { get; set; }
                internal void SetAmount(decimal value) => Amount = value;
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class CreateAssignment : Mutation<Assignment>
            {
                public required Guid SupplierId { get; init; }
                public required decimal Amount { get; init; }
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        GetGeneratorDiagnostics(result).Should().NotContain(d => d.Id == "PRAG0414",
            "SupplierId is generated by the relation, together with its SetSupplierId");
    }
}
