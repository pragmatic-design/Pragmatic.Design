using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImagingExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new ImagingException("decode failed");

        ex.Message.Should().Be("decode failed");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInner_PreservesInner()
    {
        var inner = new InvalidOperationException("root cause");

        var ex = new ImagingException("wrapper", inner);

        ex.Message.Should().Be("wrapper");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Type_IsException()
    {
        new ImagingException("x").Should().BeAssignableTo<Exception>();
    }
}
