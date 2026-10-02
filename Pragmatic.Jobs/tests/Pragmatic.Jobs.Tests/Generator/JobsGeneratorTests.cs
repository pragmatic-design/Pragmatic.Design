using Pragmatic.Testing.Assertions;

namespace Pragmatic.Jobs.Tests.Generator;

public class JobsGeneratorTests : JobsGeneratorTestBase
{
    private const string CommonUsings = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;
        using Pragmatic.Resilience.Attributes;
        """;

    /// <summary>
    ///     One job, two schedules: two definitions, each with the cron and the id it was given.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         "Every day at 02:00 <b>and</b> every Monday at 06:00" is an ordinary schedule with no
    ///         declarative form: the attribute was <c>AllowMultiple = false</c>, so the second one was
    ///         <c>CS0579</c> on the author's line and the only ways out were two classes with the same
    ///         body, or one body that re-decides at run time whether this occurrence is the one it
    ///         wanted.
    ///     </para>
    ///     <para>
    ///         ⚠️ The second schedule has to <b>name itself</b>. The default id is derived from the class
    ///         (<c>ReportJob</c> → <c>report</c>) and two schedules of one class would collide on it;
    ///         suffixing by position would invent a name nobody chose, put it in front of an operator
    ///         reading the schedule list, and renumber it silently the day the attributes are reordered
    ///         — and the id is a persisted key. So the generator asks, and <c>PRAG2507</c> says so.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task RecurringJob_WithTwoSchedules_GeneratesOneDefinitionEach()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 2 * * *")]
            [RecurringJob("0 6 * * 1", Id = "report-weekly", TimeZone = "Europe/Rome")]
            public partial class ReportJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            "two schedules on one job is a declaration, not a second class");

        var recurring = GetGeneratedSource(result, "_Infra.Jobs.RecurringJobs");

        recurring.Should().Contain("0 2 * * *");
        recurring.Should().Contain("0 6 * * 1");
        recurring.Should().Contain("\"report\"", "the first schedule keeps the id derived from the class");
        recurring.Should().Contain("\"report-weekly\"", "and the second carries the one it declared");
        recurring.Should().Contain("Europe/Rome", "the timezone belongs to the schedule, not to the job");

        // Both definitions name the same job type: it is one job on two clocks, not two jobs.
        System.Text.RegularExpressions.Regex.Matches(recurring!, "TestApp.Processing.ReportJob").Count
            .Should().Be(2);
    }

    /// <summary>A second schedule that does not name itself is refused, not silently renumbered.</summary>
    [Fact]
    public async Task RecurringJob_WithASecondScheduleAndNoId_ReportsPRAG2507()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 2 * * *")]
            [RecurringJob("0 6 * * 1")]
            public partial class SilentJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        HasDiagnostic(RunGenerator(source), "PRAG2507").Should().BeTrue(
            "two schedules would collide on the id derived from the class name");
    }

    /// <summary>The control: one schedule still produces exactly one definition, id unchanged.</summary>
    /// <remarks>
    ///     Without this, "two definitions" would be satisfied by a generator that emitted a spurious
    ///     second one for every job, and the derived id could have grown a suffix nobody asked for.
    /// </remarks>
    [Fact]
    public async Task RecurringJob_WithOneSchedule_StillGeneratesExactlyOne()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 2 * * *")]
            public partial class SoloJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var recurring = GetGeneratedSource(RunGenerator(source), "_Infra.Jobs.RecurringJobs");

        System.Text.RegularExpressions.Regex.Matches(recurring!, "new RecurringJobDefinition").Count
            .Should().Be(1);
        recurring.Should().Contain("\"solo\"", "the derived id has no suffix when there is nothing to disambiguate");
    }

    [Fact]
    public async Task RecurringJob_GeneratesInvokerAndRegistration()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 2 * * *", Id = "daily-cleanup")]
            [Retry(MaxAttempts = 3)]
            public partial class DailyCleanupJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);

        // Verify all expected files are generated
        sources.Keys.Should().Contain(k => k.Contains("DailyCleanupJob.Invoker"));
        sources.Keys.Should().Contain(k => k.Contains("_Infra.Jobs.Registration"));
        sources.Keys.Should().Contain(k => k.Contains("_Infra.Jobs.TypeRegistry"));
        sources.Keys.Should().Contain(k => k.Contains("_Infra.Jobs.RecurringJobs"));
        sources.Keys.Should().Contain(k => k.Contains("_Metadata.Jobs"));

        // Verify content patterns
        var registration = GetGeneratedSource(result, "_Infra.Jobs.Registration");
        registration.Should().Contain("AddDiscoveredJobs");
        registration.Should().Contain("DailyCleanupJob");

        // The registry must actually be contributed: without it the composite has no source and
        // every job is refused as "Unknown job type".
        registration.Should().Contain("IJobTypeRegistry");
        registration.Should().Contain("PragmaticJobTypeRegistry");
        registration.Should().Contain("IRecurringJobProvider");

        var recurring = GetGeneratedSource(result, "_Infra.Jobs.RecurringJobs");
        recurring.Should().Contain("daily-cleanup");
        recurring.Should().Contain("0 2 * * *");
        recurring.Should().Contain("IRecurringJobProvider");

        var metadata = GetGeneratedSource(result, "_Metadata.Jobs");
        metadata.Should().Contain("MetadataCategory.Jobs");
        metadata.Should().Contain("PragmaticJobRegistration.AddDiscoveredJobs");

        // No diagnostics for valid code
        GetGeneratorDiagnostics(result).Should().BeEmpty();
        GetCompilationErrors(result).Should().BeEmpty();

        await Verify(sources);
    }

    [Fact]
    public async Task OneOffJob_WithParameters_GeneratesInvokerAndRegistry()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            public record SendEmailParams(string To, string Subject);

            [Job]
            public partial class SendEmailJob : IJob<SendEmailParams>
            {
                public Task ExecuteAsync(SendEmailParams parameters, JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);

        sources.Keys.Should().Contain(k => k.Contains("SendEmailJob.Invoker"));
        sources.Keys.Should().Contain(k => k.Contains("_Infra.Jobs.Registration"));
        sources.Keys.Should().Contain(k => k.Contains("_Infra.Jobs.TypeRegistry"));

        var registry = GetGeneratedSource(result, "_Infra.Jobs.TypeRegistry");
        registry.Should().Contain("SendEmailParams");

        // A one-off-only assembly declares no recurring provider.
        var registration = GetGeneratedSource(result, "_Infra.Jobs.Registration");
        registration.Should().Contain("PragmaticJobTypeRegistry");
        registration.Should().NotContain("IRecurringJobProvider");

        GetGeneratorDiagnostics(result).Should().BeEmpty();
        GetCompilationErrors(result).Should().BeEmpty();

        await Verify(sources);
    }

    [Fact]
    public void Job_WithPriorityAndMaxConcurrency_ExposesThemInTheRegistry()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job(Priority = 7, MaxConcurrency = 2)]
            public partial class HeavyJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        var registry = GetGeneratedSource(result, "_Infra.Jobs.TypeRegistry");
        registry.Should().Contain("int GetPriority(");
        registry.Should().Contain("\"TestApp.Processing.HeavyJob\" => 7");
        registry.Should().Contain("int GetMaxConcurrency(");
        registry.Should().Contain("\"TestApp.Processing.HeavyJob\" => 2");

        GetGeneratorDiagnostics(result).Should().BeEmpty();
        GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void RecurringJob_WithMisfirePolicy_EmitsItInTheProvider()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 9 * * *", Id = "morning-digest", Misfire = MisfirePolicy.Skip)]
            public partial class MorningDigestJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        var recurring = GetGeneratedSource(result, "_Infra.Jobs.RecurringJobs");
        recurring.Should().Contain("MisfirePolicy = (global::Pragmatic.Jobs.Attributes.MisfirePolicy)1");

        GetGeneratorDiagnostics(result).Should().BeEmpty();
        GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void RecurringJob_DefaultMisfire_IsOmittedFromTheProvider()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 9 * * *", Id = "morning-digest")]
            public partial class MorningDigestJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        // RunOnce is the default; the definition object initializer stays clean when unspecified.
        GetGeneratedSource(result, "_Infra.Jobs.RecurringJobs").Should().NotContain("MisfirePolicy");
        GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void Diagnostic_NotAJob_EmitsPRAG2500()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job]
            public partial class NotAJob
            {
                public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG2500").Should().BeTrue(
            "a [Job] class that implements no job interface must be reported, not left to fail as CS1061");

        // No invoker is emitted for it, so the diagnostic is not buried under generated-code errors.
        GetAllGeneratedSources(result).Keys.Should().NotContain(k => k.Contains("NotAJob.Invoker"));
    }

    [Fact]
    public void Diagnostic_ContinuationToNonJob_EmitsPRAG2505()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            public class NotAJobAtAll { }

            [Job]
            [Continuation<NotAJobAtAll>]
            public partial class FirstJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG2505").Should().BeTrue();
    }

    [Fact]
    public void Continuation_Valid_IsExposedByTheRegistry()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job]
            [Continuation<SecondJob>]
            public partial class FirstJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }

            [Job]
            public partial class SecondJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        // The attribute must reach runtime, not only cycle detection, or a declared continuation
        // silently never runs.
        var registry = GetGeneratedSource(result, "_Infra.Jobs.TypeRegistry");
        registry.Should().Contain("GetContinuationJobType");
        registry.Should().Contain("\"TestApp.Processing.FirstJob\" => \"TestApp.Processing.SecondJob\"");

        GetGeneratorDiagnostics(result).Should().BeEmpty();
        GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void Diagnostic_NonPartial_HasNoGeneratorCopyOfPRAG2502()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job]
            public class NotPartialJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        // PRAG2502 is the companion analyzer's, on the declaration.
        HasDiagnostic(result, "PRAG2502").Should().BeFalse();
    }

    [Fact]
    public void Diagnostic_EmptyCron_EmitsPRAG2501()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("")]
            public partial class BadCronJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2501").Should().BeTrue();
    }

    [Fact]
    public void Diagnostic_DuplicateRecurringId_EmitsPRAG2503()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [RecurringJob("0 * * * *", Id = "same-id")]
            public partial class FirstJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }

            [RecurringJob("0 */2 * * *", Id = "same-id")]
            public partial class SecondJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2503").Should().BeTrue();
    }

    [Fact]
    public void Diagnostic_RetryZero_EmitsPRAG2504()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job]
            [Retry(MaxAttempts = 0)]
            public partial class BadRetryJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2504").Should().BeTrue();
    }

    [Fact]
    public void Diagnostic_ContinuationCycle_EmitsPRAG2506()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job]
            [Continuation<JobB>]
            public partial class JobA : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }

            [Job]
            [Continuation<JobA>]
            public partial class JobB : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2506").Should().BeTrue();
    }

    [Fact]
    public void Diagnostic_SelfLoop_EmitsPRAG2506()
    {
        var source = CommonUsings + """

            namespace TestApp.Processing;

            [Job]
            [Continuation<SelfLoopJob>]
            public partial class SelfLoopJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2506").Should().BeTrue();
    }
}
