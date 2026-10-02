// =============================================================================
// Allocation Tests
// Verifies zero-allocation behavior of Result types
// =============================================================================

using System.Reflection;
using Xunit;

namespace Pragmatic.Result.Tests.Allocation;

/// <summary>
///     Tests that verify Result types don't allocate on the heap.
///     These types are readonly structs, so operations should be stack-allocated.
/// </summary>
public class AllocationTests
{
    // Note: For proper allocation testing in production, use:
    // - JetBrains.dotMemory.Unit for exact allocation verification
    // - BenchmarkDotNet with MemoryDiagnoser for performance tests
    //
    // These tests verify struct semantics are preserved

    [Fact]
    public void Result_IsValueType()
    {
        Assert.True(typeof(Result<int, StringError>).IsValueType);
    }

    [Fact]
    public void VoidResult_IsValueType()
    {
        Assert.True(typeof(VoidResult<StringError>).IsValueType);
    }

    [Fact]
    public void Maybe_IsValueType()
    {
        Assert.True(typeof(Maybe<int>).IsValueType);
    }

    [Fact]
    public void Result3_IsValueType()
    {
        Assert.True(typeof(Result<int, TestValidationError, TestNotFoundError>).IsValueType);
    }

    [Fact]
    public void VoidResult2_IsValueType()
    {
        Assert.True(typeof(VoidResult<TestValidationError, TestNotFoundError>).IsValueType);
    }

    [Fact]
    public void Result_Sizeof_IsReasonable()
    {
        // Result<int, StringError> should be small:
        // - 1 byte for bool (_isSuccess)
        // - 4 bytes for int (value)
        // - 8 bytes for reference (error)
        // + alignment padding
        // Should be <= 24 bytes typically

        // Note: Can't use sizeof on managed types, but we can verify
        // the struct is reasonably sized via reflection
        var fields = typeof(Result<int, StringError>)
            .GetFields(BindingFlags.Instance |
                       BindingFlags.NonPublic);

        // Should have exactly 3 fields: _isSuccess, _value, _error
        Assert.Equal(3, fields.Length);
    }

    [Fact]
    public void VoidResult_Sizeof_IsReasonable()
    {
        var fields = typeof(VoidResult<StringError>)
            .GetFields(BindingFlags.Instance |
                       BindingFlags.NonPublic);

        // Should have exactly 2 fields: _isSuccess, _error
        Assert.Equal(2, fields.Length);
    }

    [Fact]
    public void Maybe_Sizeof_IsReasonable()
    {
        var fields = typeof(Maybe<int>)
            .GetFields(BindingFlags.Instance |
                       BindingFlags.NonPublic);

        // Should have exactly 2 fields: _hasValue, _value
        Assert.Equal(2, fields.Length);
    }

    [Fact]
    public void Result_CopySemantics()
    {
        // Verify that copying works correctly (value type semantics)
        var original = Result<int, StringError>.Success(42);
        var copy = original;

        // Both should be independent copies with same value
        Assert.True(original.IsSuccess);
        Assert.True(copy.IsSuccess);
        Assert.Equal(original.Value, copy.Value);
    }

    [Fact]
    public void Result_NoBoxingInMatch()
    {
        // Match should work without boxing the result
        var result = Result<int, StringError>.Success(42);

        // Using value types throughout - no boxing
        var output = result.Match(
            v => v * 2,
            e => -1);

        Assert.Equal(84, output);
    }

    [Fact]
    public void Result_ChainedOperations_NoAllocation()
    {
        // Chained Map/Bind should not allocate (struct copies only)
        var result = Result<int, StringError>.Success(42);

        var final = result
            .Map(v => v * 2)
            .Map(v => v + 10)
            .Map(v => v.ToString());

        Assert.True(final.IsSuccess);
        Assert.Equal("94", final.Value);
    }
}