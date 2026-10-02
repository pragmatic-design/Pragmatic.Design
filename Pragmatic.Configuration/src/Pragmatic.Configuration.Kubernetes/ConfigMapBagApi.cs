using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace Pragmatic.Configuration.Kubernetes;

/// <summary>Real <see cref="IKubernetesBagApi" /> over a ConfigMap.</summary>
internal sealed class ConfigMapBagApi(IKubernetes client, string @namespace) : IKubernetesBagApi
{
    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(string name, CancellationToken ct)
    {
        try
        {
            var configMap = await client.CoreV1
                .ReadNamespacedConfigMapAsync(name, @namespace, cancellationToken: ct).ConfigureAwait(false);
            return configMap.Data is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(configMap.Data);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertAsync(string name, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var body = new V1ConfigMap
        {
            Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = @namespace },
            Data = new Dictionary<string, string>(data)
        };

        try
        {
            await client.CoreV1.ReplaceNamespacedConfigMapAsync(body, name, @namespace, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedConfigMapAsync(body, @namespace, cancellationToken: ct)
                .ConfigureAwait(false);
        }
    }
}
