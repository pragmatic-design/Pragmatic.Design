using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Attributes;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class HttpStatusAttributeTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(599)]
    public void Constructor_ValidStatusCode_SetsStatusCode(int statusCode)
    {
        var attr = new HttpStatusAttribute(statusCode);

        attr.StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Constructor_OutOfRangeStatusCode_ThrowsArgumentOutOfRange(int statusCode)
    {
        var act = () => new HttpStatusAttribute(statusCode);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
