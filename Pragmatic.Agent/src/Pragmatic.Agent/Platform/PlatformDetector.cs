namespace Pragmatic.Agent.Platform;

/// <summary>
///     Auto-detects the platform and returns the appropriate adapter.
///     Detection order: K8s → ACA → AWS ECS → Docker → nginx → IIS → fallback.
/// </summary>
internal static class PlatformDetector
{
    public static IAgentPlatformAdapter Detect(string? overridePlatform = null)
    {
        if (!string.IsNullOrEmpty(overridePlatform))
        {
            return overridePlatform.ToLowerInvariant() switch
            {
                "iis" => new IisPlatformAdapter(),
                "nginx" => new NginxPlatformAdapter(),
                "docker" => new DockerPlatformAdapter(),
                "kubernetes" or "k8s" => new KubernetesPlatformAdapter(),
                "aca" or "azure-container-apps" => new AzureContainerAppsPlatformAdapter(),
                "aws" or "ecs" or "aws-ecs" => new AwsEcsPlatformAdapter(),
                _ => new DockerPlatformAdapter() // Default fallback
            };
        }

        // Auto-detect from environment
        if (IsKubernetes())
            return new KubernetesPlatformAdapter();

        if (IsAzureContainerApps())
            return new AzureContainerAppsPlatformAdapter();

        if (IsAwsEcs())
            return new AwsEcsPlatformAdapter();

        if (IsDocker())
            return new DockerPlatformAdapter();

        if (OperatingSystem.IsWindows())
            return new IisPlatformAdapter();

        // Linux without container → nginx + systemd
        return new NginxPlatformAdapter();
    }

    private static bool IsKubernetes()
        => Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") is not null;

    private static bool IsAzureContainerApps()
        => Environment.GetEnvironmentVariable("CONTAINER_APP_NAME") is not null;

    private static bool IsAwsEcs()
        => Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4") is not null
        || Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI") is not null;

    private static bool IsDocker()
        => File.Exists("/.dockerenv")
        || (File.Exists("/proc/1/cgroup") && File.ReadAllText("/proc/1/cgroup").Contains("docker", StringComparison.OrdinalIgnoreCase));
}
