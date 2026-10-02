using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Persistence;

/// <summary>
///     <c>TenantDatabaseOptions</c> resolves to the configured instance whether it is asked for bare or
///     as <c>IOptions&lt;T&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ If <c>AddDbPerTenant</c> registered the instance and nothing else, an application asking
///         the .NET way — <c>IOptions&lt;TenantDatabaseOptions&gt;</c>, which is what
///         <c>MultiTenancyOptions</c> itself uses two lines away — would get a <b>brand new</b> object
///         with an empty template. Nothing would fail at startup. It would show up four layers later as
///         <c>Cannot extract database name from connection string for tenant 'wayland'</c> from the
///         provisioner: <c>BuildConnectionString</c> formats an empty template into a connection
///         string with no database in it.
///     </para>
///     <para>
///         ⚠️ <b>Both, and the same object.</b> Registering the instance and calling
///         <c>Configure&lt;T&gt;</c> beside it would also make both injections work — and would make them
///         two different objects, so a host mutating one would be invisible to whoever read the other.
///         That is the same class of defect one level quieter, which is why the identity is asserted
///         here and not only the values.
///     </para>
/// </remarks>
public class TheOptionsResolveTheSameEitherWayTests
{
    private const string Template = "Host=shared;Database=tenant_{0}";

    private static ServiceProvider Container()
        => new ServiceCollection()
            .AddLogging()
            .AddDbPerTenant(o =>
            {
                o.DefaultConnectionString = "Host=shared;Database=app";
                o.ConnectionStringTemplate = Template;
            })
            .BuildServiceProvider();

    /// <summary>The setpoint: the .NET way of asking gets what was configured.</summary>
    [Fact]
    public void AskingForIOptions_GetsTheConfiguredOne()
    {
        using var container = Container();

        container.GetRequiredService<IOptions<TenantDatabaseOptions>>()
            .Value.ConnectionStringTemplate.Should().Be(Template,
                "an empty template here formats a connection string with no database in it, and says so "
                + "four layers away in whoever opens the connection");
    }

    /// <summary>
    ///     ⚠️ The control: the two ways of asking are the <b>same object</b>.
    /// </summary>
    /// <remarks>
    ///     "Both resolve" is satisfied by two instances that happen to carry the same values today and
    ///     drift the moment anything writes to one of them. The framework's own consumers inject it bare
    ///     (<c>TenantConnectionResolver</c>, <c>TenantConnectionStringProvider</c>) and an application
    ///     may inject either.
    /// </remarks>
    [Fact]
    public void TheTwoWaysOfAsking_AreOneObject()
    {
        using var container = Container();

        container.GetRequiredService<IOptions<TenantDatabaseOptions>>().Value
            .Should().BeSameAs(container.GetRequiredService<TenantDatabaseOptions>());
    }

    /// <summary>
    ///     ⚠️ And a template nobody configured is refused where it is used, not formatted into nonsense.
    /// </summary>
    /// <remarks>
    ///     The second half of this issue, and the part that survives it: registering both forms closes
    ///     <em>this</em> way of ending up with an empty template, and the class of defect — a DI
    ///     registration that reads correctly and resolves to something empty — recurs. A
    ///     <c>TenantDatabaseOptions</c> with no template is never valid, so the failure belongs at the
    ///     call that needs it, naming it, rather than in a driver reading a connection string with no
    ///     database.
    /// </remarks>
    [Fact]
    public void AnEmptyTemplate_IsRefusedByName()
    {
        var act = () => new TenantDatabaseOptions().BuildConnectionString("acme");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(nameof(TenantDatabaseOptions.ConnectionStringTemplate),
                "the message names the thing to set, not the symptom");
    }

    /// <summary>The control on that: a configured template still builds.</summary>
    /// <remarks>
    ///     "It refuses an empty one" is satisfied by refusing every one, which would take db-per-tenant
    ///     with it.
    /// </remarks>
    [Fact]
    public void AConfiguredTemplate_StillBuilds()
    {
        using var container = Container();

        container.GetRequiredService<TenantDatabaseOptions>()
            .BuildConnectionString("acme").Should().Be("Host=shared;Database=tenant_acme");
    }
}
