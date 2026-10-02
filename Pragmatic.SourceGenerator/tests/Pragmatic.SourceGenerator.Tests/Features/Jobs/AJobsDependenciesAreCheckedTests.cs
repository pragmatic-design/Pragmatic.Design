using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Jobs;

/// <summary>
///     A job is resolved from the container like any service, so what it asks for is checked at build
///     time like any service's.
/// </summary>
/// <remarks>
///     <para>
///         The compile-time dependency check runs over jobs as well as <c>[Service]</c> classes. The
///         generated registration writes <c>TryAddScoped&lt;TJob&gt;()</c> for every job, so without the
///         check a job whose dependency nothing registers would fail only when the scheduler first ran
///         it: in a background worker, on a schedule, as a log line — the place where a missing
///         registration is hardest to notice.
///     </para>
///     <para>
///         ⚠️ A report on a contract that <em>is</em> registered — by its own package's <c>Add*</c>
///         call — means the contract is missing <c>[ProvidedByHost]</c>, not that the job is wrong.
///         <c>IJobScheduler</c> and <c>INotificationService</c> carry it for that reason.
///     </para>
/// </remarks>
public class AJobsDependenciesAreCheckedTests
{
    private const string NotRegistered = "PRAG1641";

    private static string Source(string dependency) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;

        namespace TestApp;

        public interface INothingRegistersThis;

        [RecurringJob("0 7 * * *")]
        public sealed partial class ChaseOverdueInvoicesJob : IJob
        {
            public ChaseOverdueInvoicesJob({{dependency}} dependency) { }

            public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void AJobThatAsksForSomethingNobodyRegisters_IsRefusedAtBuildTime()
        => IdsOf(Run(Source("INothingRegistersThis"))).Should().Contain(NotRegistered,
            "the scheduler resolves the job from the container, and the row that says who provides "
            + "this is missing — a 500 in a worker, at seven in the morning, otherwise");

    /// <summary>
    ///     The control, and the one that says this is not "refuse every job": a contract the host
    ///     registers says so on itself, and the job that takes it is refused nothing.
    /// </summary>
    [Fact]
    public void AJobThatAsksForAContractTheHostProvides_IsNotReported()
        => IdsOf(Run(Source("global::Pragmatic.Storage.IFileStorage"))).Should().NotContain(NotRegistered);

    /// <summary>The second control: a job that asks for nothing is refused nothing.</summary>
    [Fact]
    public void AJobWithNoDependencies_IsNotReported()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Jobs;
            using Pragmatic.Jobs.Attributes;

            namespace TestApp;

            [RecurringJob("0 7 * * *")]
            public sealed partial class QuietJob : IJob
            {
                public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
            }
            """;

        IdsOf(Run(source)).Should().NotContain(NotRegistered);
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<ServiceAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Jobs.IJob>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Storage.IFileStorage>(),
        GeneratorTestHelper.FromType<global::Microsoft.Extensions.DependencyInjection.IServiceCollection>()
    ];
}
