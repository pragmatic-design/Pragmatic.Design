using Pragmatic.Testing.Assertions;
using Pragmatic.Identity.Local.Services;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class HmacSecurityTokenServiceTests
{
    private readonly HmacSecurityTokenService _service = new();

    [Fact]
    public void GenerateToken_ReturnsBase64String()
    {
        var token = _service.GenerateToken();

        token.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(token).Should().HaveCount(32);
    }

    [Fact]
    public void GenerateToken_ProducesUniqueTokens()
    {
        var token1 = _service.GenerateToken();
        var token2 = _service.GenerateToken();

        token1.Should().NotBe(token2);
    }

    [Fact]
    public void VerifyToken_WithCorrectToken_ReturnsTrue()
    {
        var token = _service.GenerateToken();
        var hash = _service.HashToken(token);

        _service.VerifyToken(token, hash).Should().BeTrue();
    }

    [Fact]
    public void VerifyToken_WithWrongToken_ReturnsFalse()
    {
        var token = _service.GenerateToken();
        var hash = _service.HashToken(token);
        var wrongToken = _service.GenerateToken();

        _service.VerifyToken(wrongToken, hash).Should().BeFalse();
    }
}
