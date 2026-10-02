using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.ZeroAllocation;

namespace Pragmatic.Logging.Tests.ZeroAllocation;

public class StackAllocatedBufferTests
{
    [Fact]
    public void WithBuffer_SmallSize_ShouldUseStackAllocation()
    {
        // Arrange
        const int size = 100; // Well below threshold
        bool actionCalled = false;

        // Act
        StackAllocatedBuffer.WithBuffer(size, buffer =>
        {
            actionCalled = true;
            buffer.Length.Should().Be(size);

            // Verify we can write to the buffer
            buffer[0] = 'A';
            buffer[0].Should().Be('A');
        });

        // Assert
        actionCalled.Should().BeTrue();
    }

    [Fact]
    public void WithBuffer_LargeSize_ShouldUseArrayPool()
    {
        // Arrange
        const int size = 2000; // Above threshold
        bool actionCalled = false;

        // Act
        StackAllocatedBuffer.WithBuffer(size, buffer =>
        {
            actionCalled = true;
            buffer.Length.Should().Be(size);

            // Verify we can write to the buffer
            for (int i = 0; i < size; i++)
            {
                buffer[i] = (char)('A' + (i % 26));
            }

            buffer[0].Should().Be('A');
            buffer[25].Should().Be('Z');
        });

        // Assert
        actionCalled.Should().BeTrue();
    }



    [Fact]
    public void StackAllocThreshold_ShouldBeReasonable()
    {
        // Assert - Verify the threshold is set to a reasonable value
        StackAllocatedBuffer.StackAllocThreshold.Should().Be(1024);
    }

    [Fact]
    public void WithBuffer_AtThreshold_ShouldUseStackAllocation()
    {
        // Arrange - Use exactly the threshold size
        int size = StackAllocatedBuffer.StackAllocThreshold;
        bool actionCalled = false;

        // Act
        StackAllocatedBuffer.WithBuffer(size, buffer =>
        {
            actionCalled = true;
            buffer.Length.Should().Be(size);
        });

        // Assert
        actionCalled.Should().BeTrue();
    }

    [Fact]
    public void WithBuffer_JustOverThreshold_ShouldUseArrayPool()
    {
        // Arrange - Use just over the threshold
        int size = StackAllocatedBuffer.StackAllocThreshold + 1;
        bool actionCalled = false;

        // Act
        StackAllocatedBuffer.WithBuffer(size, buffer =>
        {
            actionCalled = true;
            buffer.Length.Should().Be(size);
        });

        // Assert
        actionCalled.Should().BeTrue();
    }
}