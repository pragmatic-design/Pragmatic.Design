using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs;
using Pragmatic.Serialization;

namespace Pragmatic.Jobs.Tests.Unit;

public class JobContinuationTests
{
    // Dummy jobs for testing
    private sealed class NextJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    public record NextParams(string Value);

    private sealed class NextParamJob : IJob<NextParams>
    {
        public Task ExecuteAsync(NextParams parameters, JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void Then_Parameterless_SetsJobType()
    {
        var continuation = JobContinuation.Then<NextJob>();

        continuation.JobType.Should().Contain("NextJob");
        continuation.ParametersJson.Should().BeNull();
    }

    [Fact]
    public void Then_WithParams_SerializesJson()
    {
        var continuation = JobContinuation.Then<NextParamJob, NextParams>(new NextParams("hello"));

        continuation.JobType.Should().Contain("NextParamJob");
        continuation.ParametersJson.Should().Contain("hello");
    }

    [Fact]
    public void Then_WithSuppliedOptions_HonoursDisabledReflectionFallback()
    {
        // The AOT contract, and the sharpest evidence that the supplied instance is the one consulted:
        // an options object with the reflection fallback off and no context covering NextParams cannot
        // serialize it. PragmaticJsonOptions.Default, with reflection on, would have succeeded — so this
        // throwing is what distinguishes "used the argument" from "used the static default".
        var aot = new PragmaticJsonOptions().DisableReflectionFallback();

        var act = () => JobContinuation.Then<NextParamJob, NextParams>(new NextParams("hello"), aot);

        // The message names the resolver that was actually consulted, which is the assertion's real
        // subject: the seeded baseline of the supplied instance, with no reflection resolver behind it.
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*PragmaticCommonJsonContext*");
    }

    [Fact]
    public void Then_WithSuppliedOptions_RejectsNull()
    {
        var act = () => JobContinuation.Then<NextParamJob, NextParams>(new NextParams("hello"), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("jsonOptions");
    }

    [Fact]
    public void Then_WithoutOptions_FallsBackToTheStaticDefault()
    {
        // Documents what the parameterless overload is stuck with: no container to resolve from, so a
        // host's configuration cannot reach it and the reflection-enabled default serializes instead.
        var continuation = JobContinuation.Then<NextParamJob, NextParams>(new NextParams("hello"));

        continuation.ParametersJson.Should().Contain("hello");
    }
}
