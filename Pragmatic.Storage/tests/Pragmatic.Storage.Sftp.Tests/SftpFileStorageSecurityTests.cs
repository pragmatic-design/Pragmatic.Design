using System.IO;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers the path-traversal guard of <see cref="SftpFileStorage" />: a container (on save) or a
///     URI path (on read) that escapes the storage root is rejected with
///     <see cref="ArgumentException" /> before any connection is opened.
/// </summary>
public sealed class SftpFileStorageSecurityTests : SftpStorageTestBase
{
    [Theory]
    [InlineData("..")]
    [InlineData("../etc")]
    [InlineData("photos/../../etc")]
    [InlineData("/etc")]
    [InlineData("a\\b")]
    [InlineData("")]
    public async Task SaveAsync_UnsafeContainer_ThrowsArgumentException(string container)
    {
        var storage = CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        var act = () => storage.SaveAsync(stream, "file.bin", container);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("container");
        Factory.Create.DidNotReceive();
    }

    [Fact]
    public async Task GetAsync_NonSftpUri_ThrowsArgumentException()
    {
        var storage = CreateStorage();

        var act = () => storage.GetAsync(new Uri("https://evil.example.com/photos/x.png"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
        Factory.Create.DidNotReceive();
    }

    /// <summary>
    ///     Every read of a foreign URI refuses, not only <c>GetAsync</c>.
    /// </summary>
    /// <remarks>
    ///     The four methods resolve the URI through one private method, so this holds by construction —
    ///     which is exactly why it was untested for three of them. A provider that refused from one and
    ///     answered from another would be the divergence this contract closes, at a smaller scale, and
    ///     nothing here would have noticed.
    /// </remarks>
    [Fact]
    public async Task EveryReadOfAForeignUri_ThrowsArgumentExceptionWithoutConnecting()
    {
        var storage = CreateStorage();
        var foreign = new Uri("https://evil.example.com/photos/x.png");

        await ((Func<Task>)(() => storage.ExistsAsync(foreign))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => storage.DeleteAsync(foreign))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => storage.GetInfoAsync(foreign))).Should().ThrowAsync<ArgumentException>();

        Factory.Create.DidNotReceive();
    }
}
