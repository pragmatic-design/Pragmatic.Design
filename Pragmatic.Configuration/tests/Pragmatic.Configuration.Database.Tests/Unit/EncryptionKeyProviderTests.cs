using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Database.Encryption;

namespace Pragmatic.Configuration.Database.Tests.Unit;

public sealed class EncryptionKeyProviderTests : IDisposable
{
    private static readonly byte[] RawKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static string Base64Key => Convert.ToBase64String(RawKey);

    private readonly List<string> _envVarsToClear = [];

    // =========================================================================
    // InlineEncryptionKeyProvider
    // =========================================================================

    [Fact]
    public async Task Inline_WithOptionKey_DecodesBase64()
    {
        var provider = new InlineEncryptionKeyProvider(
            global::Microsoft.Extensions.Options.Options.Create(new DatabaseConfigurationOptions { EncryptionKey = Base64Key }));

        var key = await provider.GetKeyAsync();

        key.Should().Equal(RawKey);
    }

    [Fact]
    public async Task Inline_NoKeyAnywhere_Throws()
    {
        ClearEnv("PRAGMATIC_SECRET_KEY");
        var provider = new InlineEncryptionKeyProvider(
            global::Microsoft.Extensions.Options.Options.Create(new DatabaseConfigurationOptions { EncryptionKey = null }));

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Inline_InvalidBase64_Throws()
    {
        var provider = new InlineEncryptionKeyProvider(
            global::Microsoft.Extensions.Options.Options.Create(new DatabaseConfigurationOptions { EncryptionKey = "not valid base64 !!!" }));

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // =========================================================================
    // SecretStoreEncryptionKeyProvider
    // =========================================================================

    [Fact]
    public async Task SecretStore_WithKeyInStore_DecodesBase64()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISecretStore>(new FakeSecretStore { Value = Base64Key })
            .BuildServiceProvider();
        var provider = new SecretStoreEncryptionKeyProvider(services, "db-key");

        var key = await provider.GetKeyAsync();

        key.Should().Equal(RawKey);
    }

    [Fact]
    public async Task SecretStore_NoStoreRegistered_Throws()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var provider = new SecretStoreEncryptionKeyProvider(services, "db-key");

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SecretStore_SecretNotFound_Throws()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISecretStore>(new FakeSecretStore { Value = null })
            .BuildServiceProvider();
        var provider = new SecretStoreEncryptionKeyProvider(services, "db-key");

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SecretStore_InvalidBase64_Throws()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISecretStore>(new FakeSecretStore { Value = "###" })
            .BuildServiceProvider();
        var provider = new SecretStoreEncryptionKeyProvider(services, "db-key");

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SecretStore_BlankSecretName_ThrowsArgument(string? name)
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var act = () => new SecretStoreEncryptionKeyProvider(services, name!);

        act.Should().Throw<ArgumentException>();
    }

    // =========================================================================
    // ISecretStore.GetSecretWithMetadataAsync (DIM default)
    // =========================================================================

    [Fact]
    public async Task GetSecretWithMetadata_DefaultImplementation_WrapsValueWithNoExpiry()
    {
        ISecretStore store = new FakeSecretStore { Value = "the-secret" };

        var entry = await store.GetSecretWithMetadataAsync("k");

        entry.Value.Should().Be("the-secret");
        entry.Found.Should().BeTrue();
        entry.ExpiresAt.Should().BeNull();
        entry.IsExpired().Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretWithMetadata_MissingSecret_ReportsNotFound()
    {
        ISecretStore store = new FakeSecretStore { Value = null };

        var entry = await store.GetSecretWithMetadataAsync("k");

        entry.Value.Should().BeNull();
        entry.Found.Should().BeFalse();
    }

    private void ClearEnv(string name)
    {
        _envVarsToClear.Add(name);
        Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var name in _envVarsToClear)
            Environment.SetEnvironmentVariable(name, null);
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        public string? Value { get; init; }

        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Value);

        public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
            => Task.FromResult(Value);
    }
}
