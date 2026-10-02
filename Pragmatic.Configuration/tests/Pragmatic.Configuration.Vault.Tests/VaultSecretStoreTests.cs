using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Configuration.Vault.Tests;

/// <summary>End-to-end secret roundtrip against a real Vault KV v2 engine.</summary>
public sealed class VaultSecretStoreTests(VaultFixture fixture) : IClassFixture<VaultFixture>
{
    private (ISecretStore Read, IWritableSecretStore Write) Build()
    {
        var services = new ServiceCollection();
        services.AddVaultSecretStore(o =>
        {
            o.Address = fixture.Address!;
            o.Token = VaultFixture.Token;
            o.MountPoint = "secret";
            o.PathPrefix = "pragmatic";
        });
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var read = sp.GetRequiredService<ISecretStore>();
        return (read, (IWritableSecretStore)read);
    }

    [Fact]
    public async Task Set_Get_Delete_RoundTrips()
    {
        if (fixture.Address is null)
            return; // Docker unavailable — skip.

        var (read, write) = Build();

        (await read.GetSecretAsync("db-password")).Should().BeNull("secret does not exist yet");

        await write.SetSecretAsync("db-password", "s3cret");
        (await read.GetSecretAsync("db-password")).Should().Be("s3cret");

        await write.SetSecretAsync("db-password", "rotated");
        (await read.GetSecretAsync("db-password")).Should().Be("rotated", "a write overwrites the value");

        await write.DeleteSecretAsync("db-password");
        (await read.GetSecretAsync("db-password")).Should().BeNull("delete removes the secret");
    }

    [Fact]
    public async Task TenantSecret_IsIsolatedFromBase()
    {
        if (fixture.Address is null)
            return;

        var (read, write) = Build();

        await write.SetSecretAsync("api-key", "base-value");
        await write.SetSecretAsync("api-key", "tenant-value", "tenant-A");

        (await read.GetSecretAsync("api-key")).Should().Be("base-value");
        (await read.GetSecretAsync("api-key", "tenant-A")).Should().Be("tenant-value");
        (await read.GetSecretAsync("api-key", "tenant-B")).Should().BeNull("a tenant without its own secret has none");
    }

    [Fact]
    public async Task Delete_MissingSecret_IsIdempotent()
    {
        if (fixture.Address is null)
            return;

        var (_, write) = Build();

        var act = () => write.DeleteSecretAsync("never-existed");
        await act.Should().NotThrowAsync();
    }
}
