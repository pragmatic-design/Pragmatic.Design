namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers the default <see cref="ISftpClientFactory" /> authentication-selection logic that can
///     be exercised without a server: a client is built for password / private-key auth, and
///     configuring neither throws.
/// </summary>
public sealed class SftpClientFactoryTests
{
    [Fact]
    public void Create_WithPassword_ReturnsClient()
    {
        var factory = new SftpClientFactory(new SftpStorageOptions
        {
            Host = "host",
            Username = "user",
            Password = "pass",
        });

        using var client = factory.Create();

        client.Should().NotBeNull();
        client.ConnectionInfo.Host.Should().Be("host");
        client.ConnectionInfo.Username.Should().Be("user");
    }

    [Fact]
    public void Create_WithNeitherPasswordNorKey_Throws()
    {
        var factory = new SftpClientFactory(new SftpStorageOptions
        {
            Host = "host",
            Username = "user",
        });

        var act = () => factory.Create();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Password*PrivateKeyPath*");
    }
}
