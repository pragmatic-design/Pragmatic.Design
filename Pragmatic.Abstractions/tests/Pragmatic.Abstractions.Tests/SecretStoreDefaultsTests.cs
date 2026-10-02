using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class SecretStoreDefaultsTests
{
    private sealed class StubSecretStore(string? value) : ISecretStore
    {
        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => Task.FromResult(value);

        public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
            => Task.FromResult(value is null ? null : $"{tenantId}:{value}");
    }

    [Fact]
    public async Task GetSecretWithMetadata_ExistingSecret_WrapsAsFound()
    {
        ISecretStore store = new StubSecretStore("s3cret");

        var entry = await store.GetSecretWithMetadataAsync("k");

        entry.Found.Should().BeTrue();
        entry.Value.Should().Be("s3cret");
        entry.ExpiresAt.Should().BeNull("the default wrap carries no expiry metadata");
    }

    [Fact]
    public async Task GetSecretWithMetadata_MissingSecret_IsNotFound()
    {
        ISecretStore store = new StubSecretStore(null);

        (await store.GetSecretWithMetadataAsync("k")).Found.Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretWithMetadata_TenantOverload_DelegatesToTenantVariant()
    {
        ISecretStore store = new StubSecretStore("s3cret");

        var entry = await store.GetSecretWithMetadataAsync("k", "t1");

        entry.Value.Should().Be("t1:s3cret");
    }
}
