using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace Pragmatic.Configuration.Aws;

/// <summary>
///     <see cref="IConfigurationStore" /> backed by AWS SSM Parameter Store. Parameter names are
///     <c>/{prefix}/{key}</c> (base) or <c>/{prefix}/tenants/{tenant}/{key}</c>, mapping the Pragmatic
///     <c>:</c> separator to the <c>/</c> hierarchy. Parameter Store has no native change stream, so
///     <see cref="WatchAsync" /> yields nothing (hot-reload is not supported by this backend).
/// </summary>
public sealed class AwsParameterStore(IAmazonSimpleSystemsManagement client, AwsConfigurationOptions options)
    : IConfigurationStore
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => ReadAsync(ParamName(key, null), ct);

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => ReadAsync(ParamName(key, tenantId), ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => ReadSectionAsync(prefix, null, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string tenantId, CancellationToken ct = default)
        => ReadSectionAsync(prefix, tenantId, ct);

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => await client.PutParameterAsync(
                new PutParameterRequest
                {
                    Name = ParamName(key, tenantId),
                    Value = value,
                    Type = ParameterType.String,
                    Overwrite = true
                }, ct)
            .ConfigureAwait(false);

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        try
        {
            await client.DeleteParameterAsync(
                new DeleteParameterRequest { Name = ParamName(key, tenantId) }, ct).ConfigureAwait(false);
        }
        catch (ParameterNotFoundException)
        {
            // Idempotent: deleting a missing parameter is a no-op.
        }
    }

    public IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default)
        => EmptyStream();

    private async Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        try
        {
            var response = await client.GetParameterAsync(
                new GetParameterRequest { Name = name, WithDecryption = true }, ct).ConfigureAwait(false);
            return response.Parameter.Value;
        }
        catch (ParameterNotFoundException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadSectionAsync(
        string prefix, string? tenantId, CancellationToken ct)
    {
        var path = AwsNaming.SectionPath(options.PathPrefix, tenantId, prefix);
        var result = new Dictionary<string, string>();

        string? token = null;
        do
        {
            var response = await client.GetParametersByPathAsync(
                new GetParametersByPathRequest
                {
                    Path = path,
                    Recursive = true,
                    WithDecryption = true,
                    NextToken = token
                }, ct).ConfigureAwait(false);

            foreach (var parameter in response.Parameters)
                result[AwsNaming.ToLogicalKey(parameter.Name, options.PathPrefix, tenantId)] = parameter.Value;

            token = response.NextToken;
        }
        while (!string.IsNullOrEmpty(token));

        return result;
    }

    private static async IAsyncEnumerable<ConfigurationChange> EmptyStream()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private string ParamName(string key, string? tenantId) => AwsNaming.ParameterName(options.PathPrefix, tenantId, key);
}
