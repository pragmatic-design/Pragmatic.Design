using Pragmatic.Composition.Steps;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     The third of the three orders. Authentication (91) and tenant resolution (92) each assert their
///     order; authorization is the value that decides whether a permission sees its tenant. Run
///     together with authentication, a permission provider reading tenant-scoped data would run while
///     the tenant is still null and cache the empty answer, and the symptom would be a 403 on a
///     permission just granted.
/// </summary>
/// <remarks>
///     ⚠️ A test asserting a constant has nothing to say when the number has to move. The culture
///     middleware resolves at 93 and this step runs at 94, because the 403 this middleware writes is
///     the only error an application answers without an endpoint, and it has to be written with a
///     culture. What the step owes is a <em>relation</em> — after the tenant, not a magic number — so
///     that is what is asserted.
/// </remarks>
public class AuthorizationStepTests
{
    /// <summary>
    ///     After tenant resolution (92): a permission can depend on which tenant is being asked about,
    ///     so the tenant has to be resolved before authorization runs. The tenant step lives in
    ///     <c>Pragmatic.MultiTenancy.AspNetCore</c>, which this assembly does not reference, so its
    ///     order is named here as the number the two packages agree on.
    /// </summary>
    [Fact]
    public void Order_RunsAfterTenantResolution()
    {
        const int tenantResolution = 92;

        new AuthorizationStep().Order.Should().BeGreaterThan(tenantResolution);
    }

    /// <summary>
    ///     And after authentication, which is the reason the two are separate steps at all.
    /// </summary>
    [Fact]
    public void Order_RunsAfterAuthentication()
    {
        new AuthorizationStep().Order.Should().BeGreaterThan(new AuthenticationStep().Order);
    }
}
