using Pragmatic.Jobs.Attributes;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     One declaration of <c>[Retry]</c>, <c>[Timeout]</c> and <c>[CircuitBreaker]</c>, and two engines
///     that read it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The using directives at the top of this file are the test.</b> Were the three
///         attributes declared twice — once in <c>Pragmatic.Jobs.Attributes</c>, once in
///         <c>Pragmatic.Messaging.Attributes</c> — importing both namespaces and naming
///         <c>Retry</c> would be <c>CS0104</c>, and this file would not compile.
///     </para>
///     <para>
///         The loud failure is not the point. The quiet one is copying
///         <c>[Retry(MaxAttempts = 3)]</c> from a job to a handler: with two declarations it reads
///         identically and applies a different engine, so the base delay silently changes.
///     </para>
/// </remarks>
public class OneRetryDeclarationForBothEnginesTests
{
    /// <summary>A type that a job engine and a message engine could both be asked to run.</summary>
    [Retry(MaxAttempts = 4, Strategy = BackoffStrategy.Fixed, BaseDelayMs = 750)]
    [Timeout(TimeoutSeconds = 45)]
    [CircuitBreaker(FailureThreshold = 7, BreakDurationSeconds = 90)]
    private sealed class AJobThatIsAlsoAHandler;

    /// <summary>Both namespaces imported, and one type answers to <c>Retry</c>.</summary>
    [Fact]
    public void WithBothNamespacesImported_OneDeclarationAnswersToRetry()
    {
        typeof(RetryAttribute).FullName
            .Should().Be("Pragmatic.Resilience.Attributes.RetryAttribute",
                "the module named for the concept is where the concept is declared");

        typeof(BackoffStrategy).FullName
            .Should().Be("Pragmatic.Resilience.Attributes.BackoffStrategy");
    }

    /// <summary>What is written on the declaration is what the declaration carries.</summary>
    [Fact]
    public void WhatIsWritten_IsWhatTheDeclarationCarries()
    {
        var retry = (RetryAttribute)Attribute.GetCustomAttribute(
            typeof(AJobThatIsAlsoAHandler), typeof(RetryAttribute))!;

        retry.MaxAttempts.Should().Be(4);
        retry.Strategy.Should().Be(BackoffStrategy.Fixed);
        retry.BaseDelayMs.Should().Be(750);
    }

    /// <summary>
    ///     The control: the shared declaration carries no default of its own.
    /// </summary>
    /// <remarks>
    ///     Without it, one declaration serving two engines is satisfied by a declaration that picks one
    ///     module's numbers — 1000 ms for a job, 200 ms for a redelivery — and silently changes the
    ///     other's behaviour. Both are deliberate, so neither belongs on the attribute: an unwritten
    ///     property stays unset, and each engine applies its own when it reads.
    /// </remarks>
    [Fact]
    public void WhatIsNotWritten_StaysUnsetInsteadOfPickingAnEngineSDefault()
    {
        var retry = new RetryAttribute();

        retry.MaxAttempts.Should().Be(0, "unset, not three");
        retry.BaseDelayMs.Should().Be(0, "unset — not 1000 for a job and not 200 for a redelivery");
        retry.Strategy.Should().Be(BackoffStrategy.Unspecified, "unset, not Exponential");

        new TimeoutAttribute().TimeoutSeconds
            .Should().Be(0, "unset — not 300 for a job and not 30 for a handler");
    }
}
