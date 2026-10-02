using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Gcp.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
public class GcpConfigurationExtensionsTests
{
    /// <remarks>
    ///     Registration only: resolving the store creates a Secret Manager client from Application
    ///     Default Credentials, which a unit test has no business having.
    /// </remarks>
    [Fact]
    public void AddGcpSecretStore_RegistersTheSecretStore()
    {
        var services = new ServiceCollection();

        services.AddGcpSecretStore(o => o.ProjectId = "my-project");

        services.Should().Contain(d => d.ServiceType == typeof(ISecretStore));
    }

    /// <remarks>
    ///     Without a project there is nothing to read secrets from, and the failure would otherwise
    ///     surface as an authentication error against an unnamed project.
    /// </remarks>
    [Fact]
    public void AddGcpSecretStore_WithoutAProjectId_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddGcpSecretStore(_ => { });

        act.Should().Throw<InvalidOperationException>();
    }
}
