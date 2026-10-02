using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers container path-traversal rejection: a malicious container must be refused with
///     <see cref="ArgumentException" /> before any connection is opened.
/// </summary>
public sealed class FtpFileStorageSecurityTests
{
    [Theory]
    [InlineData("..")]
    [InlineData("../etc")]
    [InlineData("photos/../../etc")]
    [InlineData("/rooted")]
    [InlineData("back\\slash")]
    [InlineData("C:drive")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_InvalidContainer_ThrowsArgumentExceptionWithoutConnecting(string container)
    {
        var ctx = FtpStorageTestContext.Create();
        using var stream = new MemoryStream([1, 2, 3]);

        var act = () => ctx.Storage.SaveAsync(stream, "photo.jpg", container);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("container");
        ctx.Factory.Create.DidNotReceive();
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
        var ctx = FtpStorageTestContext.Create();
        var foreign = new Uri("https://evil.example.com/photos/x.jpg");

        await ((Func<Task>)(() => ctx.Storage.ExistsAsync(foreign))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => ctx.Storage.DeleteAsync(foreign))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => ctx.Storage.GetInfoAsync(foreign))).Should().ThrowAsync<ArgumentException>();

        ctx.Factory.Create.DidNotReceive();
    }
}
