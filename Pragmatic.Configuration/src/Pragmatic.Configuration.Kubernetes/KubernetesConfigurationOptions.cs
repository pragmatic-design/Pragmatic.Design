namespace Pragmatic.Configuration.Kubernetes;

/// <summary>Options for the Kubernetes configuration/secret backends.</summary>
public sealed class KubernetesConfigurationOptions
{
    /// <summary>Kubernetes namespace holding the ConfigMap/Secret objects. Default <c>default</c>.</summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    ///     Object name (base) for the backing ConfigMap/Secret (e.g. <c>pragmatic-config</c>). Tenant entries
    ///     live in a per-tenant object <c>{name}-{tenant}</c>.
    /// </summary>
    public string ObjectName { get; set; } = "pragmatic-config";

    /// <summary>
    ///     Optional explicit kubeconfig path. When null the client uses in-cluster config (if running in a pod)
    ///     or the default kubeconfig resolution.
    /// </summary>
    public string? KubeConfigPath { get; set; }
}
