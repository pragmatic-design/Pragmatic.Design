using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     What the invoker logs for one action, as a provider receives it: id, name, level, message and the
///     state's pairs. The values are those Microsoft's generator produced for these methods, so moving them
///     to Pragmatic's call sites (<c>PragmaticLogCallSites</c>) changes none of them for whoever filters,
///     alerts or queries on them.
/// </summary>
public class TheInvokerKeepsItsLogEventsTests
{
    private sealed class PlaceOrder : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class PlaceOrderInvoker(IServiceProvider sp) : DomainActionInvoker<PlaceOrder, string>(sp)
    {
        protected override void InjectDependencies(PlaceOrder action) { }
    }

    [Fact]
    public async Task ASuccessfulActionLogsStartExecutingAndSuccess_AsMicrosoftsGeneratorDid()
    {
        var recorder = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        await using var provider = services.BuildServiceProvider();

        await new PlaceOrderInvoker(provider).InvokeAsync(new PlaceOrder());

        var entries = recorder.Entries.ToArray();

        var start = entries.Single(e => e.EventId.Name == "LogStart");
        start.EventId.Id.Should().Be(1092114269);
        start.Level.Should().Be(LogLevel.Debug);
        start.Message.Should().Be("Action PlaceOrder starting");
        start.Pairs.Select(p => p.Key).Should().Equal("kind", "actionName", "{OriginalFormat}");

        var executing = entries.Single(e => e.EventId.Name == "LogExecuting");
        executing.EventId.Id.Should().Be(328106247);
        executing.Level.Should().Be(LogLevel.Debug);
        executing.Message.Should().Be("Action PlaceOrder executing");

        var success = entries.Single(e => e.EventId.Name == "LogSuccess");
        success.EventId.Id.Should().Be(1535103852);
        success.Level.Should().Be(LogLevel.Information);
        Regex.IsMatch(success.Message, @"^Action PlaceOrder succeeded in \d+ms$").Should().BeTrue(success.Message);
        success.Pairs.Select(p => p.Key).Should().Equal("kind", "actionName", "elapsedMs", "{OriginalFormat}");
        success.Pairs[^1].Value.Should().Be("{kind} {actionName} succeeded in {elapsedMs}ms");
        success.Pairs[2].Value.Should().BeOfType<long>();
    }
}
