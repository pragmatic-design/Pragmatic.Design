using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for [LoadEntity&lt;T&gt;] source generation.
/// </summary>
public class LoadEntityGeneratorTests : ActionsGeneratorTestBase
{
    [Fact]
    public void Action_WithLoadEntity_GeneratesFieldAndSetter()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         public class Reservation : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                             public string GuestName { get; set; } = "";
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("Id")]
                         public partial class ConfirmReservationAction : VoidDomainAction
                         {
                             public required System.Guid Id { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        var generated = GetGeneratedSource(result, "LoadEntity");
        generated.Should().NotBeNull("LoadEntity partial should be generated");
        generated.Should().Contain("_reservation");
        generated.Should().Contain("SetLoadedEntities");
        generated.Should().Contain("Reservation");
    }

    [Fact]
    public void Action_WithLoadEntity_InvokerIncludesPrepare()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         public class Reservation : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("Id")]
                         public partial class ConfirmReservationAction : VoidDomainAction
                         {
                             public required System.Guid Id { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull("Invoker should be generated");
        invokerSource.Should().Contain("PrepareActionAsync");
        invokerSource.Should().Contain("GetByIdAsync");
        invokerSource.Should().Contain("NotFoundError.For");
        invokerSource.Should().Contain("SetLoadedEntities");
    }

    [Fact]
    public void Action_WithMultipleLoadEntities_GeneratesAll()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         public class Reservation : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                         }

                         public class Guest : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                             public string Name { get; set; } = "";
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("ReservationId")]
                         [LoadEntity<TestApp.Entities.Guest>("GuestId")]
                         public partial class CheckInAction : VoidDomainAction
                         {
                             public required System.Guid ReservationId { get; init; }
                             public required System.Guid GuestId { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        var loadEntitySource = GetGeneratedSource(result, "LoadEntity");
        loadEntitySource.Should().NotBeNull();
        loadEntitySource.Should().Contain("_reservation");
        loadEntitySource.Should().Contain("_guest");
        loadEntitySource.Should().Contain("SetLoadedEntities");

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();
        invokerSource.Should().Contain("_reservationRepository");
        invokerSource.Should().Contain("_guestRepository");
    }

    [Fact]
    public void Action_WithLoadEntity_InvalidPropertyName_ReportsDiagnostic()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         public class Reservation : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("NonExistentProperty")]
                         public partial class BadAction : VoidDomainAction
                         {
                             public required System.Guid Id { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0404").Should().BeTrue(
            "should report PRAG0404 when ID property is not found");
    }

    /// <summary>
    ///     PRAG0405, the sibling of the one above: the id property is there, but the type it is meant
    ///     to load is not an entity, so there is no key to compare it against.
    /// </summary>
    /// <remarks>
    ///     A null key type is "not an entity", not "unknown key type", and the difference is
    ///     load-bearing: the load is skipped entirely, the generated action carries no repository, and
    ///     the field the author expected to read is never assigned. Without a test of its own, a
    ///     diagnostic is indistinguishable from a disabled one.
    /// </remarks>
    [Fact]
    public void Action_WithLoadEntity_OnATypeThatIsNotAnEntity_ReportsPrag0405()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         // No [Entity], no IEntity: a plain class someone pointed [LoadEntity] at.
                         public class Reservation
                         {
                             public System.Guid PersistenceId { get; set; }
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("Id")]
                         public partial class BadAction : VoidDomainAction
                         {
                             public required System.Guid Id { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0405").Should().BeTrue(
            "the key type cannot be determined for a type that is not an entity");
    }

    /// <summary>The control: the same action against a real entity is silent.</summary>
    [Fact]
    public void Action_WithLoadEntity_OnAnEntity_ReportsNoKeyTypeDiagnostic()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         public class Reservation : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("Id")]
                         public partial class GoodAction : VoidDomainAction
                         {
                             public required System.Guid Id { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0405").Should().BeFalse();
    }

    [Fact]
    public void Action_WithLoadEntity_ExistingRepository_DeduplicatesDependency()
    {
        var source = """
                     using Pragmatic.Actions.Abstractions;
                     using Pragmatic.Actions.Attributes;
                     using Pragmatic.Persistence.Entity;
                     using Pragmatic.Persistence.Repository;
                     using Pragmatic.Result;

                     namespace TestApp.Entities
                     {
                         public class Reservation : IEntity
                         {
                             public System.Guid PersistenceId { get; set; }
                         }
                     }

                     namespace TestApp.Actions
                     {
                         [DomainAction]
                         [LoadEntity<TestApp.Entities.Reservation>("Id")]
                         public partial class ActionWithExistingRepo : VoidDomainAction
                         {
                             private IReadRepository<TestApp.Entities.Reservation> _reservationRepository = null!;
                             public required System.Guid Id { get; init; }

                             public override System.Threading.Tasks.Task<VoidResult<IError>> Execute(
                                 System.Threading.CancellationToken ct = default)
                                 => System.Threading.Tasks.Task.FromResult(VoidResult<IError>.Success());
                         }
                     }
                     """;

        var result = RunGeneratorWithEntities(source);

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();

        // The repository field should only appear once in the invoker (not duplicated)
        // Count 'readonly' occurrences as proxy for field declarations
        var fieldDeclarations = invokerSource!.Split("readonly").Length - 1;
        fieldDeclarations.Should().Be(1,
            "repository dependency should be deduplicated — only one field declaration");
    }
}
