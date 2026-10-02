using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Resilience.Tests.Unit;

/// <summary>
///     A <c>[ResiliencePolicy("name")]</c> no configuration defines is reported once at startup, naming the
///     policy and the operations that declared it.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline for such a name is a passthrough: no retry, no breaker, no timeout, and — until
///         this — no word about it. A typo in the attribute, or a <c>Resilience</c> section missing from
///         the production settings, left the call unprotected while every declaration was in place.
///     </para>
///     <para>
///         The passthrough stays: annotating first and configuring later is a workflow the docs defend.
///         What changes is that the gap is written in the log.
///     </para>
/// </remarks>
public sealed class AnUndefinedPolicyIsReportedAtStartupTests
{
    [Fact]
    public void ADeclaredPolicyNothingDefines_IsReported_NamingThePolicyAndTheOperation()
    {
        var (provider, logs) = Build(declared: [new("missing", "TestApp.Payments.ChargeCard")]);

        UndefinedResiliencePolicies.Report(provider).Should().Equal("missing");

        logs.Should().ContainSingle(l => l.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("missing").And.Contain("TestApp.Payments.ChargeCard");
    }

    /// <summary>One warning per name, however many operations declare it.</summary>
    [Fact]
    public void TwoOperationsOnOneUndefinedName_AreOneWarning()
    {
        var (provider, logs) = Build(declared:
        [
            new("missing", "TestApp.Payments.ChargeCard"),
            new("missing", "TestApp.Payments.RefundCard"),
        ]);

        UndefinedResiliencePolicies.Report(provider);

        logs.Should().ContainSingle(l => l.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("ChargeCard").And.Contain("RefundCard");
    }

    /// <summary>The control: a policy the configuration defines is not reported.</summary>
    [Fact]
    public void ADefinedPolicy_IsNotReported()
    {
        var (provider, logs) = Build(
            declared: [new("payments", "TestApp.Payments.ChargeCard")],
            configure: o => o.Policies["payments"] = new ResiliencePolicyOptions());

        UndefinedResiliencePolicies.Report(provider).Should().BeEmpty();
        logs.Should().NotContain(l => l.Level == LogLevel.Warning);
    }

    /// <summary>The control: a <c>Default</c> answers every name, so nothing runs unprotected.</summary>
    [Fact]
    public void WithADefault_NothingIsReported()
    {
        var (provider, _) = Build(
            declared: [new("missing", "TestApp.Payments.ChargeCard")],
            configure: o => o.Default = new ResiliencePolicyOptions());

        UndefinedResiliencePolicies.Report(provider).Should().BeEmpty();
    }

    /// <summary>A policy registered fluently before the check counts as defined.</summary>
    [Fact]
    public void AFluentPolicy_IsNotReported()
    {
        var (provider, _) = Build(declared: [new("fluent", "TestApp.Payments.ChargeCard")]);
        provider.GetRequiredService<IResiliencePipelineRegistry>().AddPolicy("fluent", b => b);

        UndefinedResiliencePolicies.Report(provider).Should().BeEmpty();
    }

    private static (ServiceProvider Provider, ConcurrentQueue<Captured> Logs) Build(
        DeclaredResiliencePolicy[] declared, Action<ResilienceOptions>? configure = null)
    {
        var logs = new ConcurrentQueue<Captured>();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(logs));
        services.AddPragmaticResilience(configure);
        foreach (var declaration in declared)
            services.AddSingleton(declaration);

        return (services.BuildServiceProvider(), logs);
    }

    private sealed record Captured(LogLevel Level, string Message);

    private sealed class CapturingLoggerFactory(ConcurrentQueue<Captured> logs) : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => logs.Enqueue(new Captured(logLevel, formatter(state, exception)));

        public void Dispose()
        {
        }
    }
}
