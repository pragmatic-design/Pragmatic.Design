using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Errors;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResilienceErrorsTests
{
    // ── HedgingExhaustedError ──────────────────────────────────────────────

    [Fact]
    public void HedgingExhaustedError_HasExpectedCodeAndStatus()
    {
        var error = new HedgingExhaustedError(3);

        error.Code.Should().Be("HEDGING_EXHAUSTED");
        error.StatusCode.Should().Be(503);
    }

    [Fact]
    public void HedgingExhaustedError_Title_IncludesAttemptCount()
    {
        var error = new HedgingExhaustedError(4);

        error.Title.Should().Be("All 4 hedging attempts failed");
    }

    [Fact]
    public void HedgingExhaustedError_RecordEquality_BySameAttempts()
    {
        new HedgingExhaustedError(3).Should().Be(new HedgingExhaustedError(3));
        new HedgingExhaustedError(3).Should().NotBe(new HedgingExhaustedError(5));
    }

    // ── RateLimitRejectedError ─────────────────────────────────────────────

    [Fact]
    public void RateLimitRejectedError_HasExpectedCodeAndStatus()
    {
        var error = new RateLimitRejectedError(100, TimeSpan.FromSeconds(1));

        error.Code.Should().Be("RATE_LIMIT_REJECTED");
        error.StatusCode.Should().Be(429);
    }

    [Fact]
    public void RateLimitRejectedError_Title_IncludesLimitAndWindow()
    {
        var error = new RateLimitRejectedError(100, TimeSpan.FromSeconds(2));

        error.Title.Should().Be("Rate limit exceeded: 100 requests per 2s window");
    }

    [Fact]
    public void RateLimitRejectedError_Title_ComputedOnce_StableAcrossAccess()
    {
        var error = new RateLimitRejectedError(10, TimeSpan.FromSeconds(1));

        // Title is an init-only property assigned in the constructor — repeated reads return the same value.
        error.Title.Should().BeSameAs(error.Title);
    }

    [Fact]
    public void RateLimitRejectedError_RecordEquality_BySameValues()
    {
        var window = TimeSpan.FromSeconds(1);
        new RateLimitRejectedError(100, window).Should().Be(new RateLimitRejectedError(100, window));
        new RateLimitRejectedError(100, window).Should().NotBe(new RateLimitRejectedError(50, window));
    }

    // ── RetryExhaustedError: LastException is JsonIgnore'd ──────────────────

    [Fact]
    public void RetryExhaustedError_HasExpectedCodeAndStatus()
    {
        var error = new RetryExhaustedError("fetch", 3, null);

        error.Code.Should().Be("RETRY_EXHAUSTED");
        error.StatusCode.Should().Be(503);
    }

    [Fact]
    public void RetryExhaustedError_Title_IncludesAttemptsAndOperation()
    {
        var error = new RetryExhaustedError("fetch", 3, null);

        error.Title.Should().Be("All 3 retry attempts exhausted for 'fetch'");
    }

    [Fact]
    public void RetryExhaustedError_Serialized_DoesNotLeakLastException()
    {
        var error = new RetryExhaustedError("fetch", 3, new InvalidOperationException("internal stack trace"));

        var json = JsonSerializer.Serialize(error);

        json.Should().NotContain("internal stack trace");
        json.Should().NotContain("LastException");
    }

    [Fact]
    public void RetryExhaustedError_Serialized_StillContainsPublicContractFields()
    {
        var error = new RetryExhaustedError("checkout", 2, new TimeoutException());

        var json = JsonSerializer.Serialize(error);

        json.Should().Contain("checkout");
        json.Should().Contain("OperationName");
        json.Should().Contain("Attempts");
    }

    [Fact]
    public void RetryExhaustedError_LastException_StillAccessibleServerSide()
    {
        var inner = new InvalidOperationException("boom");
        var error = new RetryExhaustedError("op", 1, inner);

        // JsonIgnore only affects serialization; the property remains readable for diagnostics.
        error.LastException.Should().BeSameAs(inner);
    }
}
