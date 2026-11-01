using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     A MongoDB container that exits during its start fails the wait at once, with its log. The wait only counted the readiness line, so a
///     <c>mongod</c> that had exited was waited on for the full two minutes and reported as a timeout, its own reason never shown: on the runners'
///     Linux 7 kernel, MongoDB 8 refuses to start, and every suite that starts it read as slow (#406, #456).
/// </summary>
public class AMongoThatExitsFailsAtOnceTests
{
    [Fact]
    public async Task AMongodThatRefusesItsOptions_FailsTheStartWithinSeconds_WithItsLog()
    {
        // mongod rejects an unknown option and exits: what a refusal to start looks like from the outside.
        var container = MongoServer.Start(new ContainerBuilder().WithImage("mongo:9").WithCommand("--not-an-option"));
        await using var dispose = container.ConfigureAwait(false);

        var clock = Stopwatch.StartNew();
        var thrown = await Record.ExceptionAsync(() => container.StartAsync());
        clock.Stop();

        Assert.NotNull(thrown);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"the start failed after {clock.Elapsed}: {thrown.GetType().Name}: {thrown.Message}");
        Assert.Contains("not-an-option", thrown.Message, StringComparison.Ordinal);
    }
}
