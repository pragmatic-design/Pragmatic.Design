using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Kubernetes.Tests;

/// <summary>
///     The two ways to turn this package on.
/// </summary>
/// <remarks>
///     Asserted on the registration rather than on a resolved store: building the client reads a
///     kubeconfig or the in-cluster service account, neither of which a unit test has. What has to
///     hold is that asking for the ConfigMap store leaves an <c>IConfigurationStore</c>, and asking
///     for the Secret store leaves an <c>ISecretStore</c> — the two are separate calls, and getting
///     one when you meant the other is the mistake worth catching.
/// </remarks>
public class KubernetesConfigurationExtensionsTests
{
    [Fact]
    public void AddKubernetesConfigurationStore_RegistersTheConfigurationStore()
    {
        var services = new ServiceCollection();

        services.AddKubernetesConfigurationStore(o => o.Namespace = "default");

        services.Should().Contain(d => d.ServiceType == typeof(IConfigurationStore));
    }

    [Fact]
    public void AddKubernetesSecretStore_RegistersTheSecretStore()
    {
        var services = new ServiceCollection();

        services.AddKubernetesSecretStore(o => o.Namespace = "default");

        services.Should().Contain(d => d.ServiceType == typeof(ISecretStore));
    }
}
