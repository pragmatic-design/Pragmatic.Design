using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Configuration.Aws.Tests;

/// <summary>End-to-end secret roundtrip against LocalStack's AWS Secrets Manager.</summary>
public sealed class AwsSecretStoreTests(LocalStackFixture fixture) : IClassFixture<LocalStackFixture>
{
    private (ISecretStore Read, IWritableSecretStore Write) Build()
    {
        var services = new ServiceCollection();
        services.AddAwsSecretStore(o =>
        {
            o.ServiceUrl = fixture.ServiceUrl!;
            o.Region = LocalStackFixture.Region;
            o.AccessKey = LocalStackFixture.AccessKey;
            o.SecretKey = LocalStackFixture.SecretKey;
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
        if (fixture.ServiceUrl is null)
            return;

        var (read, write) = Build();

        (await read.GetSecretAsync("Db:Password")).Should().BeNull();

        await write.SetSecretAsync("Db:Password", "s3cret");
        (await read.GetSecretAsync("Db:Password")).Should().Be("s3cret");

        await write.SetSecretAsync("Db:Password", "rotated");
        (await read.GetSecretAsync("Db:Password")).Should().Be("rotated", "a write overwrites the value");

        await write.DeleteSecretAsync("Db:Password");
        (await read.GetSecretAsync("Db:Password")).Should().BeNull("delete removes the secret");
    }

    [Fact]
    public async Task TenantSecret_IsIsolatedFromBase()
    {
        if (fixture.ServiceUrl is null)
            return;

        var (read, write) = Build();

        await write.SetSecretAsync("api-key", "base-value");
        await write.SetSecretAsync("api-key", "tenant-value", "tenant-A");

        (await read.GetSecretAsync("api-key")).Should().Be("base-value");
        (await read.GetSecretAsync("api-key", "tenant-A")).Should().Be("tenant-value");
        (await read.GetSecretAsync("api-key", "tenant-B")).Should().BeNull();
    }

    [Fact]
    public async Task Delete_MissingSecret_IsIdempotent()
    {
        if (fixture.ServiceUrl is null)
            return;

        var (_, write) = Build();

        var act = () => write.DeleteSecretAsync("never-existed");
        await act.Should().NotThrowAsync();
    }
}
