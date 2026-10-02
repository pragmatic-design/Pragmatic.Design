using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for Mutation soft-delete auto-detection and Restore mode generation.
/// </summary>
public class MutationSoftDeleteTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    private const string SoftDeleteEntity = """

        namespace TestApp
        {
            [SoftDelete]
            public class Order : IEntity, ISoftDelete
            {
                public Guid PersistenceId { get; set; }
                public string Status { get; set; } = "Pending";
                public bool IsDeleted { get; set; }
                public DateTimeOffset? DeletedAt { get; set; }
                public string? DeletedBy { get; set; }
            }
        }
        """;

    private const string PlainEntity = """

        namespace TestApp
        {
            public class Invoice : IEntity
            {
                public Guid PersistenceId { get; set; }
                public decimal Amount { get; set; }
            }
        }
        """;

    [Fact]
    public void DeleteMutation_OnSoftDeleteEntity_AutoDetectsSoftDelete()
    {
        var source = CommonUsings + SoftDeleteEntity + """

            namespace TestApp
            {
                [Mutation(Mode = MutationMode.Delete)]
                public partial class DeleteOrder : Mutation<Order>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        // Should generate soft-delete logic (not _repository.Remove)
        invoker.Should().Contain("entity.IsDeleted = true");
        invoker.Should().Contain("entity.DeletedAt = _timeProvider.GetUtcNow();",
            "the stamp comes from the injected clock — a static one cannot be pinned");
        invoker.Should().Contain("entity.DeletedBy = _currentUser?.IdOrNull()");
        invoker.Should().NotContain("_repository.Remove(entity)");

        // Should inject ICurrentUser
        invoker.Should().Contain("ICurrentUser? _currentUser");
        invoker.Should().Contain("using Pragmatic.Identity");
    }

    [Fact]
    public void DeleteMutation_OnPlainEntity_UsesHardDelete()
    {
        var source = CommonUsings + PlainEntity + """

            namespace TestApp
            {
                [Mutation(Mode = MutationMode.Delete)]
                public partial class DeleteInvoice : Mutation<Invoice>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        // Should use hard delete
        invoker.Should().Contain("_repository.Remove(entity)");
        invoker.Should().NotContain("entity.IsDeleted = true");
        invoker.Should().NotContain("ICurrentUser");
    }

    [Fact]
    public void RestoreMutation_GeneratesRestoreEntity()
    {
        var source = CommonUsings + SoftDeleteEntity + """

            namespace TestApp
            {
                [Mutation(Mode = MutationMode.Restore)]
                public partial class RestoreOrder : Mutation<Order>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        // Should generate RestoreEntity
        invoker.Should().Contain("RestoreEntity");
        invoker.Should().Contain("entity.IsDeleted = false");
        invoker.Should().Contain("entity.DeletedAt = null");
        invoker.Should().Contain("entity.DeletedBy = null");

        // Should generate GetMode as Restore
        invoker.Should().Contain("MutationMode.Restore");

        // Should inject IQueryFilterToggle for bypass
        invoker.Should().Contain("IQueryFilterToggle? _filterToggle");

        // Never a blanket disable: that also lifted the ownership and access-scope filters. Which
        // filter the restore names instead is the subject of
        // RestoreMutation_LiftsOnlyTheSoftDeleteFilter_NotEveryFilter, on a declared [Entity].
        invoker.Should().NotContain("DisableAll()");

        // Should bypass EF Core global query filter for restore
        invoker.Should().Contain("IgnoreQueryFilters");
    }

    /// <summary>
    ///     A restore lifts the soft-delete filter by name and leaves every other filter standing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two halves of a restore load have the same width. The EF half names one
    ///         filter — <c>IgnoreQueryFilters(query, ["SoftDelete"])</c> — precisely so tenant
    ///         isolation survives. A Pragmatic half calling <c>DisableAll()</c> would be wider:
    ///         <c>QueryFilterToggle.IsDisabled</c> answers true for every filter while such a scope is
    ///         open, and ownership and access scopes are provider filters, so a restore by id would
    ///         reach and resurrect another owner's row inside the same tenant.
    ///     </para>
    ///     <para>
    ///         The narrow form is <c>Disable(typeof(…))</c> against the
    ///         entity's own generated <c>SoftDeleteFilter</c>, which is what
    ///         <c>DefaultQueryFilterProvider.IsFilterDisabled</c> compares <c>filter.GetType()</c>
    ///         against.
    ///     </para>
    /// </remarks>
    [Fact]
    public void RestoreMutation_LiftsOnlyTheSoftDeleteFilter_NotEveryFilter()
    {
        var source = CommonUsings + """

            namespace TestApp
            {
                // [Entity] as well as [SoftDelete], because the nested SoftDeleteFilter the restore
                // names comes off the entity pipeline: without it there is no filter class and no DI
                // registration either, so there would be nothing to lift.
                // No [HasOwner] here: this harness has no entity partial carrying SetOwnerId, so the
                // ownership trait cannot compile in it. The filter under test is the soft-delete one
                // either way — ownership is what must survive, and it survives by DisableAll() being
                // absent.
                [Entity]
                [SoftDelete]
                public partial class Ticket : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public string Subject { get; set; } = "";
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }
                }

                [Mutation(Mode = MutationMode.Restore)]
                public partial class RestoreTicket : Mutation<Ticket>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        // ⚠️ No HasCompilationErrors assertion here, unlike its neighbours, and not by choice: with
        // [Entity] declared this harness emits an invoker that calls the generated Ticket.Create
        // factory, and the entity pipeline does not produce that factory without a [Module] in the
        // compilation. PresetProviderRegistrationTests has the same shape for the same reason. That
        // the line compiles is measured where a real [Entity] restore runs — Showcase
        // SoftDeleteTests.RestoreProperty_ResetsDeletedFields_VisibleAgain.
        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        invoker.Should().Contain(
            "_filterToggle?.Disable(typeof(global::TestApp.Ticket.SoftDeleteFilter))",
            "the restore must name the filter it lifts, as the EF half already does");
        invoker.Should().NotContain(
            "DisableAll()",
            "a blanket disable also lifts the ownership and access-scope filters");
    }

    /// <summary>
    ///     The control: a mutation that is not a restore opens no filter scope at all.
    /// </summary>
    /// <remarks>
    ///     Without it an assertion that merely looks for the narrow line would pass on a generator
    ///     that emitted it for every mutation.
    /// </remarks>
    [Fact]
    public void UpdateMutation_OpensNoFilterScope()
    {
        var source = CommonUsings + SoftDeleteEntity + """

            namespace TestApp
            {
                [Mutation(Mode = MutationMode.Update)]
                public partial class RenameOrder : Mutation<Order>
                {
                    public required Guid Id { get; init; }
                    public required string Status { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().NotContain("_filterToggle");
        invoker.Should().NotContain("DisableAll()");
    }

    [Fact]
    public void RestoreMutation_ByNamingConvention_DetectsRestoreMode()
    {
        var source = CommonUsings + SoftDeleteEntity + """

            namespace TestApp
            {
                [Mutation]
                public partial class RestoreOrder : Mutation<Order>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("MutationMode.Restore");
        invoker.Should().Contain("RestoreEntity");
    }

    [Fact]
    public void DeleteMutation_ExplicitSoftDeleteTrue_ForcesAutoDetection()
    {
        // Entity without [SoftDelete] attribute but implementing ISoftDelete
        // Explicit SoftDelete=true on [Mutation] forces soft delete behavior
        var entityWithoutAttr = """

            namespace TestApp
            {
                public class Payment : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public decimal Amount { get; set; }
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }
                }
            }
            """;

        var source = CommonUsings + entityWithoutAttr + """

            namespace TestApp
            {
                [Mutation(Mode = MutationMode.Delete, SoftDelete = true)]
                public partial class DeletePayment : Mutation<Payment>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("entity.IsDeleted = true");
        invoker.Should().NotContain("_repository.Remove(entity)");
    }

    [Fact]
    public void DeleteMutation_WithCascade_GeneratesCascadeSoftDelete()
    {
        var source = CommonUsings + """

            namespace TestApp
            {
                public class OrderLine : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public string Product { get; set; } = "";
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }
                }

                [SoftDelete(Cascade = true)]
                public class Order : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public string Status { get; set; } = "Pending";
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }

                    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
                }

                [Mutation(Mode = MutationMode.Delete)]
                public partial class DeleteOrder : Mutation<Order>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        // Note: generated code references EntityFrameworkQueryableExtensions (Include)
        // which may not be in test references — only verify generated content
        var result = RunGeneratorWithEntities(source);

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        // Parent soft-delete
        invoker.Should().Contain("entity.IsDeleted = true");

        // Cascade to Lines collection
        invoker.Should().Contain("Cascade soft-delete");
        invoker.Should().Contain("entity.Lines is { } lines");
        invoker.Should().Contain("foreach (var item in lines)");
        invoker.Should().Contain("item.IsDeleted = true");

        // Auto-include for cascade targets
        invoker.Should().Contain("Include(query, \"Lines\")");
    }

    [Fact]
    public void DeleteMutation_WithCascade_SingleNavigation_GeneratesCascade()
    {
        var source = CommonUsings + """

            namespace TestApp
            {
                public class Profile : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public string Bio { get; set; } = "";
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }
                }

                [SoftDelete(Cascade = true)]
                public class User : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public string Name { get; set; } = "";
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }

                    public Profile? UserProfile { get; set; }
                }

                [Mutation(Mode = MutationMode.Delete)]
                public partial class DeleteUser : Mutation<User>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        // Note: generated code references EntityFrameworkQueryableExtensions (Include)
        // which may not be in test references — only verify generated content
        var result = RunGeneratorWithEntities(source);

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        // Single reference cascade
        invoker.Should().Contain("entity.UserProfile is { } userProfile");
        invoker.Should().Contain("userProfile.IsDeleted = true");
        invoker.Should().NotContain("foreach");

        // Auto-include
        invoker.Should().Contain("Include(query, \"UserProfile\")");
    }

    [Fact]
    public void DeleteMutation_NoCascade_DoesNotCascade()
    {
        var source = CommonUsings + """

            namespace TestApp
            {
                public class OrderLine : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }
                }

                [SoftDelete]
                public class Order : IEntity, ISoftDelete
                {
                    public Guid PersistenceId { get; set; }
                    public bool IsDeleted { get; set; }
                    public DateTimeOffset? DeletedAt { get; set; }
                    public string? DeletedBy { get; set; }

                    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
                }

                [Mutation(Mode = MutationMode.Delete)]
                public partial class DeleteOrder : Mutation<Order>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "MutationInvoker");
        invoker.Should().NotBeNull();

        // Soft delete parent only, no cascade
        invoker.Should().Contain("entity.IsDeleted = true");
        invoker.Should().NotContain("Cascade soft-delete");
        invoker.Should().NotContain("entity.Lines");
        invoker.Should().NotContain("foreach");
    }
}
