using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Jobs.Tests.Generator;

public class JobsDiagnosticsGeneratorTests : JobsGeneratorTestBase
{
    [Fact]
    public void RecurringJob_WithEmptyCron_ReportsPrag2501()
    {
        var source = """
            using Pragmatic.Jobs;
            using Pragmatic.Jobs.Attributes;

            [RecurringJob("")]
            public partial class EmptyCronJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        GetGeneratorDiagnostics(result).Should()
            .Contain(d => d.Id == "PRAG2501" && d.GetMessage().Contains("EmptyCronJob"));
    }

    [Fact]
    public void Job_OnNonPartialClass_HasNoGeneratorCopyOfPrag2502()
    {
        var source = """
            using Pragmatic.Jobs;
            using Pragmatic.Jobs.Attributes;

            [Job]
            public class NonPartialJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        // PRAG2502 is the companion analyzer's, on the declaration.
        GetGeneratorDiagnostics(result).Should().NotContain(d => d.Id == "PRAG2502");
    }

    [Fact]
    public void Job_OnValidPartialIJob_ReportsNoErrors()
    {
        var source = """
            using Pragmatic.Jobs;
            using Pragmatic.Jobs.Attributes;

            [Job]
            public partial class ValidJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        GetGeneratorDiagnostics(result).Should().BeEmpty();
    }
}
