// =============================================================================
// VoidResult (Non-Generic) Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     Tests for VoidResult (non-generic) which represents success/failure without error details.
/// </summary>
public class VoidResultNonGenericTests
{
    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void Success_CreatesSuccessResult()
    {
        var result = VoidResult.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Failure_CreatesFailureResult()
    {
        var result = VoidResult.Failure();

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    // =========================================================================
    // Match Methods
    // =========================================================================

    [Fact]
    public void Match_CallsOnSuccessWhenSuccess()
    {
        var result = VoidResult.Success();

        var output = result.Match(
            () => "success",
            () => "failure");

        Assert.Equal("success", output);
    }

    [Fact]
    public void Match_CallsOnFailureWhenFailure()
    {
        var result = VoidResult.Failure();

        var output = result.Match(
            () => "success",
            () => "failure");

        Assert.Equal("failure", output);
    }

    [Fact]
    public void MatchAction_CallsOnSuccessWhenSuccess()
    {
        var result = VoidResult.Success();
        var called = false;

        result.Match(
            () => called = true,
            () => called = false);

        Assert.True(called);
    }

    [Fact]
    public void MatchAction_CallsOnFailureWhenFailure()
    {
        var result = VoidResult.Failure();
        var called = false;

        result.Match(
            () => called = false,
            () => called = true);

        Assert.True(called);
    }

    [Fact]
    public void Match_ThrowsOnNullOnSuccess()
    {
        var result = VoidResult.Success();

        Assert.Throws<ArgumentNullException>(() =>
            result.Match(null!, () => "f"));
    }

    [Fact]
    public void Match_ThrowsOnNullOnFailure()
    {
        var result = VoidResult.Success();

        Assert.Throws<ArgumentNullException>(() =>
            result.Match(() => "s", null!));
    }

    [Fact]
    public void MatchAction_ThrowsOnNullOnSuccess()
    {
        var result = VoidResult.Success();

        Assert.Throws<ArgumentNullException>(() =>
            result.Match(null!, () => { }));
    }

    [Fact]
    public void MatchAction_ThrowsOnNullOnFailure()
    {
        var result = VoidResult.Success();

        Assert.Throws<ArgumentNullException>(() =>
            result.Match(() => { }, null!));
    }

    // =========================================================================
    // Then Method
    // =========================================================================

    [Fact]
    public void Then_WhenSuccess_ExecutesNext()
    {
        var result = VoidResult.Success();
        var executed = false;

        var output = result.Then(() =>
        {
            executed = true;
            return VoidResult.Success();
        });

        Assert.True(executed);
        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void Then_WhenFailure_DoesNotExecuteNext()
    {
        var result = VoidResult.Failure();
        var executed = false;

        var output = result.Then(() =>
        {
            executed = true;
            return VoidResult.Success();
        });

        Assert.False(executed);
        Assert.True(output.IsFailure);
    }

    [Fact]
    public void Then_PropagatesFailureFromNext()
    {
        var result = VoidResult.Success();

        var output = result.Then(() => VoidResult.Failure());

        Assert.True(output.IsFailure);
    }

    [Fact]
    public void Then_ThrowsOnNullNext()
    {
        var result = VoidResult.Success();

        Assert.Throws<ArgumentNullException>(() => result.Then(null!));
    }

    [Fact]
    public void Then_CanBeChained()
    {
        var log = new List<string>();

        var result = VoidResult.Success()
            .Then(() =>
            {
                log.Add("1");
                return VoidResult.Success();
            })
            .Then(() =>
            {
                log.Add("2");
                return VoidResult.Success();
            })
            .Then(() =>
            {
                log.Add("3");
                return VoidResult.Success();
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(["1", "2", "3"], log);
    }

    [Fact]
    public void Then_StopsOnFirstFailure()
    {
        var log = new List<string>();

        var result = VoidResult.Success()
            .Then(() =>
            {
                log.Add("1");
                return VoidResult.Success();
            })
            .Then(() =>
            {
                log.Add("2");
                return VoidResult.Failure();
            })
            .Then(() =>
            {
                log.Add("3");
                return VoidResult.Success();
            });

        Assert.True(result.IsFailure);
        Assert.Equal(["1", "2"], log);
    }

    // =========================================================================
    // Implicit Conversions
    // =========================================================================

    [Fact]
    public void ImplicitToBool_TrueWhenSuccess()
    {
        var result = VoidResult.Success();

        bool value = result;

        Assert.True(value);
    }

    [Fact]
    public void ImplicitToBool_FalseWhenFailure()
    {
        var result = VoidResult.Failure();

        bool value = result;

        Assert.False(value);
    }

    [Fact]
    public void ImplicitToBool_InIfStatement()
    {
        var success = VoidResult.Success();
        var failure = VoidResult.Failure();

        var successResult = success ? "yes" : "no";
        var failureResult = failure ? "yes" : "no";

        Assert.Equal("yes", successResult);
        Assert.Equal("no", failureResult);
    }

    [Fact]
    public void ExplicitFromBool_TrueCreatesSuccess()
    {
        var result = (VoidResult)true;

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ExplicitFromBool_FalseCreatesFailure()
    {
        var result = (VoidResult)false;

        Assert.True(result.IsFailure);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void VoidResult_IsValueType()
    {
        Assert.True(typeof(VoidResult).IsValueType);
    }

    [Fact]
    public void VoidResult_DefaultIsFailure()
    {
        // Default struct has IsSuccess = false (default bool)
        var result = default(VoidResult);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void VoidResult_CanBeUsedInCollections()
    {
        var results = new List<VoidResult>
        {
            VoidResult.Success(),
            VoidResult.Failure(),
            VoidResult.Success()
        };

        Assert.Equal(2, results.Count(r => r.IsSuccess));
        Assert.Single(results, r => r.IsFailure);
    }

    // =========================================================================
    // Equality (#14)
    // =========================================================================

    [Fact]
    public void Equality_TwoSuccesses_AreEqual()
    {
        Assert.True(VoidResult.Success() == VoidResult.Success());
        Assert.True(VoidResult.Success().Equals(VoidResult.Success()));
        Assert.False(VoidResult.Success() != VoidResult.Success());
    }

    [Fact]
    public void Equality_TwoFailures_AreEqual()
    {
        Assert.True(VoidResult.Failure() == VoidResult.Failure());
        Assert.Equal(VoidResult.Failure().GetHashCode(), VoidResult.Failure().GetHashCode());
    }

    [Fact]
    public void Equality_SuccessAndFailure_AreNotEqual()
    {
        Assert.True(VoidResult.Success() != VoidResult.Failure());
        Assert.False(VoidResult.Success().Equals(VoidResult.Failure()));
    }

    [Fact]
    public void Equality_BoxedObject_UsesValueEquality()
    {
        object boxed = VoidResult.Success();

        Assert.True(boxed.Equals(VoidResult.Success()));
        Assert.False(boxed.Equals(VoidResult.Failure()));
    }

    // =========================================================================
    // IResultBase (#14)
    // =========================================================================

    [Fact]
    public void IResultBase_Success_ExposesConsistentShape()
    {
        IResultBase result = VoidResult.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.HasValueType);
        Assert.Null(result.ValueAsObject);
        Assert.Null(result.ErrorAsObject);
    }

    [Fact]
    public void IResultBase_Failure_ExposesConsistentShape()
    {
        IResultBase result = VoidResult.Failure();

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.False(result.HasValueType);
        Assert.Null(result.ValueAsObject);
        Assert.Null(result.ErrorAsObject);
    }
}