using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Auditing.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
/// <remarks>
///     The handlers exist and are covered; what was not covered is that calling
///     <c>AddIdentitySecurityAuditing</c> puts them where the dispatcher looks. A handler nobody
///     registers is a handler nobody runs, and the failure is silence — the login still fails, the
///     account still locks, and no entry is written.
/// </remarks>
public class IdentityAuditingExtensionsTests
{
    [Fact]
    public void AddIdentitySecurityAuditing_RegistersAHandlerForEachAuditedEvent()
    {
        var services = new ServiceCollection();

        services.AddIdentitySecurityAuditing();

        services.Should().Contain(d => d.ServiceType == typeof(IDomainEventHandler<LoginFailed>),
            "a failed login is the event the trail exists for");
        services.Should().Contain(d => d.ServiceType == typeof(IDomainEventHandler<AccountLocked>));
    }

    /// <remarks>
    ///     Registered as enumerable on purpose: contributing a handler must never displace whatever
    ///     else already listens to the same event.
    /// </remarks>
    [Fact]
    public void AddIdentitySecurityAuditing_CalledTwice_DoesNotDuplicateTheHandlers()
    {
        var services = new ServiceCollection();

        services.AddIdentitySecurityAuditing();
        services.AddIdentitySecurityAuditing();

        services.Count(d => d.ServiceType == typeof(IDomainEventHandler<LoginFailed>)).Should().Be(1);
    }
}
