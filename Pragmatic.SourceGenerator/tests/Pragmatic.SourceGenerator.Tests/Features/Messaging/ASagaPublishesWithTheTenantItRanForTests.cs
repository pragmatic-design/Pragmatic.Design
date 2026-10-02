using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     The generated saga repository registration hands the repository the <b>tenant context</b>, so a
///     message a saga publishes carries the tenant the step ran for.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The parameter is optional:
///         <c>EfCoreSagaRepository(dbContext, logger, jsonOptions = null, tenantContext = null)</c>. A
///         registration that passes <b>three</b> arguments compiles, and then <c>tenantContext</c> is
///         always <see langword="null" />, <c>SaveWithStepAndOutboxAsync</c> writes
///         <c>TenantId = tenantContext?.TenantId</c> as null, and every message a saga publishes leaves
///         without a tenant. Declared and wired to nothing.
///     </para>
///     <para>
///         ⚠️⚠️ <b>What it costs is invisible until something writes.</b> With the tenant interceptor
///         fail-closed, the handler that receives such a message cannot write: the operation fails, the
///         handler throws, the delivery is retried and dead-lettered. In <b>one</b> scope the saga
///         instance is written with the ambient tenant — the interceptor stamps it — and the outbox row
///         beside it with <c>&lt;null&gt;</c>, because that one line reads an injected service instead.
///         The work downstream never happens, the saga reports success about it, and the only trace is
///         one message in the dead-letter queue.
///     </para>
/// </remarks>
public class ASagaPublishesWithTheTenantItRanForTests
{
    private const string DbContext = "global::MyApp.Ordering.Entities.OrderingDbContext";

    private static SagaModel Saga() => new()
    {
        Namespace = "MyApp.Ordering",
        TypeName = "OrderSaga",
        Accessibility = "public",
        TypeKind = "class",
        IsPartial = true,
        AssemblyName = "MyApp.Ordering",
        ImplementsISaga = true,
        PersistenceDbContextFqn = DbContext,
        StateTypeFqn = "global::MyApp.Ordering.OrderState",
        StateTypeShortName = "OrderState",
        StateValues = ImmutableArray.Create("Created", "Completed"),
        StartStep = Step(),
        Steps = ImmutableArray.Create(Step()),
    };

    private static SagaStepModel Step() => new()
    {
        MethodName = "HandleOrderRequested",
        EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
        EventTypeShortName = "OrderRequested",
        IsStart = true,
        NextState = "Completed",
    };

    private static string Render()
        => new SagaRegistrationTemplate(ImmutableArray.Create(Saga()), hasEfCore: true).RenderOutput().Text;

    /// <summary>The setpoint.</summary>
    [Fact]
    public void TheRepository_IsGivenTheTenantContext()
        => Render().Should().Contain(
            "sp.GetService<global::Pragmatic.MultiTenancy.ITenantContext>()",
            "a message a saga publishes has to carry the tenant the step ran for, and the repository "
            + "reads it from this service rather than from the ambient scope");

    /// <summary>
    ///     ⚠️ Resolved with <c>GetService</c> and not <c>GetRequiredService</c>.
    /// </summary>
    /// <remarks>
    ///     The parameter is optional because a single-tenant application registers no tenant context at
    ///     all, and a saga there must still run. Requiring it would turn "this app has one tenant" into a
    ///     startup failure.
    /// </remarks>
    [Fact]
    public void TheTenantContext_IsOptional()
        => Render().Should().NotContain(
            "GetRequiredService<global::Pragmatic.MultiTenancy.ITenantContext>",
            "a single-tenant application registers none and its sagas still have to run");

    /// <summary>
    ///     ⚠️ The control: the other two arguments are still there, in order.
    /// </summary>
    /// <remarks>
    ///     The arguments are positional and two of the four are optional, so an argument inserted in the
    ///     wrong place compiles and means something else. "The tenant context is passed" is satisfied by
    ///     a call that passes it as the JSON options.
    /// </remarks>
    [Fact]
    public void TheOtherArguments_AreStillThereAndInOrder()
    {
        var source = Render();

        var dbContext = source.IndexOf("__marker.DbContextType", StringComparison.Ordinal);
        var json = source.IndexOf("PragmaticJsonOptions>()", StringComparison.Ordinal);
        var tenant = source.IndexOf("ITenantContext>()", StringComparison.Ordinal);

        dbContext.Should().BeGreaterThan(-1);
        json.Should().BeGreaterThan(dbContext, "the JSON options come after the context and the logger");
        tenant.Should().BeGreaterThan(json, "and the tenant context is the fourth argument, after them");
    }

    /// <summary>
    ///     A saga with no EF persistence gets the in-memory repository and no tenant context to pass.
    /// </summary>
    /// <remarks>
    ///     The control on the branch: the registration above is the EF one, and asserting on the whole
    ///     file would pass for a template that emitted the argument everywhere.
    /// </remarks>
    [Fact]
    public void TheInMemoryRepository_TakesNone()
    {
        var source = new SagaRegistrationTemplate(
            ImmutableArray.Create(Saga() with { PersistenceDbContextFqn = null }), hasEfCore: false)
            .RenderOutput().Text;

        source.Should().Contain("InMemorySagaRepository");
        source.Should().NotContain("ITenantContext");
    }
}
