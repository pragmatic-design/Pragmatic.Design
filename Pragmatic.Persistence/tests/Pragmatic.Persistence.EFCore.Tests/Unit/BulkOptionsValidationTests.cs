using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.EFCore.Bulk;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     A non-positive BatchSize makes the batching loop never advance
///     (offset += BatchSize stays at 0) → infinite loop. The options must reject it eagerly.
/// </summary>
public class BulkOptionsValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void BulkInsertOptions_NonPositiveBatchSize_Throws(int batchSize)
    {
        var act = () => new BulkInsertOptions { BatchSize = batchSize };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void UpsertOptions_NonPositiveBatchSize_Throws(int batchSize)
    {
        var act = () => new UpsertOptions { BatchSize = batchSize };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BulkInsertOptions_PositiveBatchSize_IsAccepted()
    {
        var options = new BulkInsertOptions { BatchSize = 500 };

        options.BatchSize.Should().Be(500);
    }

    [Fact]
    public void UpsertOptions_DefaultBatchSize_Is1000()
    {
        new UpsertOptions().BatchSize.Should().Be(1000);
    }
}
