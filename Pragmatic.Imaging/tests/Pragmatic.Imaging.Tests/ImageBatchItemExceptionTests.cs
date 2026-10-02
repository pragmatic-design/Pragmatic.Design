using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImageBatchItemExceptionTests
{
    [Fact]
    public void Constructor_SetsIndexAndWrapsInner()
    {
        var inner = new InvalidOperationException("boom");

        var ex = new ImageBatchItemException(3, inner);

        ex.Index.Should().Be(3);
        ex.InnerException.Should().BeSameAs(inner);
        ex.Message.Should().Contain("index 3").And.Contain("boom");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void Index_RoundTripsConstructorArgument(int index)
    {
        var ex = new ImageBatchItemException(index, new Exception("e"));

        ex.Index.Should().Be(index);
    }
}
