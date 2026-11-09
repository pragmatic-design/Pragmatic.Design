using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Which entity a transition contract is about, when another type carries the same state.
/// </summary>
/// <remarks>
///     ⚠️ Across assemblies the endpoint's private repository field is gone, and the extractor fell back to the
///     first type with a property of the state enum whose name did not read as a projection. A query's row
///     (<c>TenantListRow</c>) is not on that list, so the contracts were named <c>TenantListRow_TransitionTo…</c>
///     and asked the application to arrange a <c>"TenantListRow"</c>. The operation already says which entity it
///     works on — <c>Mutation&lt;TEntity&gt;</c>, <c>[LoadEntity&lt;T&gt;]</c> — and both survive into a reference
///     assembly.
/// </remarks>
public partial class StateTransitionGenerationTests
{
    /// <summary>The state, the entity that owns it, and a query row that carries it too, declared first.</summary>
    private const string TenantWithAListRow = """
        namespace App.Admin.Enums
        {
            public enum TenantStatus
            {
                [Pragmatic.Persistence.StateMachine.InitialState]
                Active,
                [Pragmatic.Persistence.StateMachine.TransitionFrom(Active)]
                Suspended
            }
        }
        namespace App.Admin.Queries
        {
            public class TenantListRow { public App.Admin.Enums.TenantStatus Status { get; set; } }
        }
        namespace App.Admin.Tenants
        {
            public class Tenant { public App.Admin.Enums.TenantStatus Status { get; set; } }
        }
        """;

    private const string SuspendAsAMutation = """
        namespace App.Admin.Tenants.Mutations
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/tenants/{id}/suspend")]
            [Pragmatic.Authorization.RequirePermission("admin.tenant.update")]
            [Pragmatic.Actions.Attributes.TransitionsTo<App.Admin.Enums.TenantStatus>(App.Admin.Enums.TenantStatus.Suspended)]
            public class SuspendTenantMutation : Pragmatic.Actions.Mutation.Mutation<App.Admin.Tenants.Tenant>
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Admin.Tenants.Tenant> _tenants;
                public System.Guid Id { get; set; }
            }
        }
        """;

    private const string SuspendAsAnActionThatLoadsTheTenant = """
        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class LoadEntityAttribute<TEntity> : System.Attribute where TEntity : class
            {
                public LoadEntityAttribute(string idPropertyName) { }
            }
        }
        namespace App.Admin.Tenants.Actions
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/tenants/{id}/suspend")]
            [Pragmatic.Authorization.RequirePermission("admin.tenant.update")]
            [Pragmatic.Actions.Attributes.TransitionsTo<App.Admin.Enums.TenantStatus>(App.Admin.Enums.TenantStatus.Suspended)]
            [Pragmatic.Actions.Attributes.LoadEntity<App.Admin.Tenants.Tenant>("Id")]
            public class SuspendTenantAction : Pragmatic.Actions.Action.DomainAction<bool>
            {
                private Pragmatic.Persistence.Repository.IRepository<App.Admin.Tenants.Tenant> _tenants;
                public System.Guid Id { get; set; }
            }
        }
        """;

    [Fact]
    public void AMutationTransition_IsNamedAfterItsEntity_NotAQueryRowWithTheSameState()
    {
        var source = AcrossAssemblies(TenantWithAListRow + SuspendAsAMutation, "Transitions");

        source.Should().Contain("Tenant_TransitionToSuspended", "the mutation says it works on Tenant");
        source.Should().NotContain("TenantListRow", "a query row carrying the state is not the entity");
    }

    [Fact]
    public void AnActionTransition_IsNamedAfterTheEntityItLoads_NotAQueryRowWithTheSameState()
    {
        var source = AcrossAssemblies(TenantWithAListRow + SuspendAsAnActionThatLoadsTheTenant, "Transitions");

        source.Should().Contain("Tenant_TransitionToSuspended", "[LoadEntity<Tenant>] says which entity moves");
        source.Should().NotContain("TenantListRow", "a query row carrying the state is not the entity");
    }
}
