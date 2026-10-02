using Microsoft.Extensions.Logging;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Logging;
using Xunit;

namespace Pragmatic.Testing.Tests.Logging;

/// <summary>
///     Asserting on what an application logged.
/// </summary>
/// <remarks>
///     The framework requires <c>[LoggerMessage]</c> and gave no way to verify any of it, so nobody
///     verified any of it. These tests exist because a lab application needed to prove that a
///     delegated write names both the subject and the actor — a claim that had been made in a skill
///     and never observed.
/// </remarks>
public class CapturedLogsTests
{
    private static (CapturedLogs Logs, ILogger Logger) Arrange()
    {
        var logs = new CapturedLogs();
        return (logs, logs.CreateLogger("Test.Category"));
    }

    [Fact]
    public void ARenderedLine_IsCaptured()
    {
        var (logs, logger) = Arrange();

        logger.LogInformation("Story {WorkItemId} written by {ActorId}", 7, "agent-7");

        logs.Contains(LogLevel.Information, "Story 7 written by agent-7").Should().BeTrue();
    }

    [Fact]
    public void TheStructuredProperties_AreKept_NotOnlyTheText()
    {
        var (logs, logger) = Arrange();

        logger.LogInformation("Story {WorkItemId} written by {ActorId}", 7, "agent-7");

        logs.PropertyOf("Story", "ActorId").Should().Be("agent-7",
            "the value of [LoggerMessage] is the named fields, not the sentence");
    }

    /// <summary>
    ///     The matcher requires the property, not just the text.
    /// </summary>
    /// <remarks>
    ///     The first version of this helper matched on text alone and picked up an invoker's own
    ///     "Action WriteStoryAction succeeded" line — which contains <c>Story</c> and carries no such
    ///     property — returning null. That reads exactly like "the application never logged it", and
    ///     cost a round of looking for a defect that was in the test helper.
    /// </remarks>
    [Fact]
    public void ANeighbouringLineWithTheSameWord_DoesNotShadowTheRealOne()
    {
        var (logs, logger) = Arrange();

        logger.LogInformation("Story {WorkItemId} written by {ActorId}", 7, "agent-7");
        logger.LogInformation("Action WriteStoryAction succeeded in {Elapsed}ms", 12);

        logs.PropertyOf("Story", "ActorId").Should().Be("agent-7");
    }

    [Fact]
    public void AnAbsentProperty_IsNull_NotAnException()
    {
        var (logs, logger) = Arrange();

        logger.LogInformation("Story {WorkItemId} written", 7);

        logs.PropertyOf("Story", "ActorId").Should().BeNull();
    }

    [Fact]
    public void TheMostRecentMatch_Wins()
    {
        var (logs, logger) = Arrange();

        logger.LogInformation("Story {WorkItemId} written by {ActorId}", 1, "agent-1");
        logger.LogInformation("Story {WorkItemId} written by {ActorId}", 2, "agent-2");

        logs.PropertyOf("Story", "ActorId").Should().Be("agent-2");
    }

    [Fact]
    public void AnException_IsKept()
    {
        var (logs, logger) = Arrange();

        logger.LogError(new InvalidOperationException("boom"), "Story {WorkItemId} failed", 7);

        logs.Lines.Single().Exception!.Message.Should().Be("boom");
    }

    /// <summary>
    ///     <c>Clear</c> exists for a fixture shared across tests.
    /// </summary>
    /// <remarks>
    ///     Without it an assertion can pass on a line another test produced, which is the quietest way
    ///     for a suite to stop meaning anything.
    /// </remarks>
    [Fact]
    public void Clear_ForgetsWhatCameBefore()
    {
        var (logs, logger) = Arrange();
        logger.LogInformation("Story {WorkItemId} written by {ActorId}", 1, "agent-1");

        logs.Clear();

        logs.Lines.Should().BeEmpty();
        logs.PropertyOf("Story", "ActorId").Should().BeNull();
    }
}
