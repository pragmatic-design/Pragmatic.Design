using FluentFTP;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers <see cref="DefaultAsyncFtpClientFactory" />: it maps <see cref="FtpStorageOptions" /> to
///     a configured <see cref="AsyncFtpClient" /> without opening a connection.
/// </summary>
public sealed class DefaultAsyncFtpClientFactoryTests
{
    [Fact]
    public void Create_UseSslTrue_SetsExplicitEncryptionMode()
    {
        var factory = new DefaultAsyncFtpClientFactory(new FtpStorageOptions
        {
            Host = "ftp.example.com",
            Username = "user",
            UseSsl = true,
        });

        using var client = (AsyncFtpClient)factory.Create();

        client.Config.EncryptionMode.Should().Be(FtpEncryptionMode.Explicit);
    }

    [Fact]
    public void Create_UseSslFalse_SetsNoneEncryptionMode()
    {
        var factory = new DefaultAsyncFtpClientFactory(new FtpStorageOptions
        {
            Host = "ftp.example.com",
            Username = "user",
            UseSsl = false,
        });

        using var client = (AsyncFtpClient)factory.Create();

        client.Config.EncryptionMode.Should().Be(FtpEncryptionMode.None);
    }

    [Fact]
    public void Create_MapsHostPortAndUser()
    {
        var factory = new DefaultAsyncFtpClientFactory(new FtpStorageOptions
        {
            Host = "ftp.example.com",
            Port = 2121,
            Username = "user",
        });

        using var client = (AsyncFtpClient)factory.Create();

        client.Host.Should().Be("ftp.example.com");
        client.Port.Should().Be(2121);
        client.Credentials.UserName.Should().Be("user");
    }
}
