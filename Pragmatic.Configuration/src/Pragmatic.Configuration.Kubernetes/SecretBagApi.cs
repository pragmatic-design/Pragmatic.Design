using System.Net;
using System.Text;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace Pragmatic.Configuration.Kubernetes;

/// <summary>Real <see cref="IKubernetesBagApi" /> over a Secret (writes via <c>stringData</c>, reads decoded).</summary>
internal sealed class SecretBagApi(IKubernetes client, string @namespace) : IKubernetesBagApi
{
    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(string name, CancellationToken ct)
    {
        try
        {
            var secret = await client.CoreV1
                .ReadNamespacedSecretAsync(name, @namespace, cancellationToken: ct).ConfigureAwait(false);

            var result = new Dictionary<string, string>();
            if (secret.Data is not null)
                foreach (var (key, bytes) in secret.Data)
                    result[key] = Encoding.UTF8.GetString(bytes);
            return result;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertAsync(string name, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var body = new V1Secret
        {
            Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = @namespace },
            StringData = new Dictionary<string, string>(data)
        };

        try
        {
            await client.CoreV1.ReplaceNamespacedSecretAsync(body, name, @namespace, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedSecretAsync(body, @namespace, cancellationToken: ct)
                .ConfigureAwait(false);
        }
    }
}
