using Google.Api.Gax.ResourceNames;
using Google.Cloud.SecretManager.V1;
using Google.Protobuf;
using Grpc.Core;

namespace Pragmatic.Configuration.Gcp;

/// <summary>Real <see cref="IGcpSecretApi" /> over <see cref="SecretManagerServiceClient" />.</summary>
internal sealed class GcpSecretApi(SecretManagerServiceClient client, string projectId) : IGcpSecretApi
{
    public async Task<string?> AccessLatestAsync(string secretId, CancellationToken ct)
    {
        try
        {
            var response = await client
                .AccessSecretVersionAsync(new SecretVersionName(projectId, secretId, "latest"), ct)
                .ConfigureAwait(false);
            return response.Payload.Data.ToStringUtf8();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertAsync(string secretId, string value, CancellationToken ct)
    {
        try
        {
            await client.CreateSecretAsync(
                new ProjectName(projectId),
                secretId,
                new Secret { Replication = new Replication { Automatic = new Replication.Types.Automatic() } },
                ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Secret container already exists — just add a new version below.
        }

        await client.AddSecretVersionAsync(
            new SecretName(projectId, secretId),
            new SecretPayload { Data = ByteString.CopyFromUtf8(value) },
            ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string secretId, CancellationToken ct)
    {
        try
        {
            await client.DeleteSecretAsync(new SecretName(projectId, secretId), ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // Idempotent.
        }
    }
}
