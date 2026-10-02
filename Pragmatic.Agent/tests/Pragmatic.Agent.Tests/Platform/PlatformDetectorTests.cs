using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Platform;
using Xunit;

namespace Pragmatic.Agent.Tests.Platform;

public class PlatformDetectorTests
{
    [Theory]
    [InlineData("iis", "IIS")]
    [InlineData("nginx", "nginx")]
    [InlineData("docker", "Docker")]
    [InlineData("kubernetes", "Kubernetes")]
    [InlineData("k8s", "Kubernetes")]
    [InlineData("aca", "AzureContainerApps")]
    [InlineData("azure-container-apps", "AzureContainerApps")]
    [InlineData("aws", "AWS-ECS")]
    [InlineData("ecs", "AWS-ECS")]
    [InlineData("aws-ecs", "AWS-ECS")]
    public void Detect_WithOverride_ReturnsCorrectAdapter(string overridePlatform, string expectedName)
    {
        var adapter = PlatformDetector.Detect(overridePlatform);
        adapter.PlatformName.Should().Be(expectedName);
    }

    [Fact]
    public void Detect_UnknownOverride_FallsBackToDocker()
    {
        var adapter = PlatformDetector.Detect("unknown-platform");
        adapter.PlatformName.Should().Be("Docker");
    }

    [Fact]
    public void Detect_NullOverride_AutoDetects()
    {
        // Without K8s/ACA/ECS env vars, should detect based on OS
        var adapter = PlatformDetector.Detect(null);
        adapter.Should().NotBeNull();
        adapter.PlatformName.Should().NotBeNullOrEmpty();
    }
}
