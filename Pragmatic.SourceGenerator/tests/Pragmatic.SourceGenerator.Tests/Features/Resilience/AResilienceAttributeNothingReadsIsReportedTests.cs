using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resilience;

/// <summary>
///     <c>[Retry]</c>, <c>[Timeout]</c> and <c>[CircuitBreaker]</c> on a class no engine reads them on are
///     reported where they are written (PRAG0464).
/// </summary>
/// <remarks>
///     <para>
///         Two engines read them: jobs (<c>[Retry]</c>, <c>[Timeout]</c> on a <c>[Job]</c> or
///         <c>[RecurringJob]</c>) and message handlers (all three on a <c>[MessageHandler]</c>). On a
///         <c>[DomainAction]</c> the attribute compiled, was counted as a resilience declaration — the
///         host even wired <c>AddPragmaticResilience()</c> for it — and the action ran once.
///     </para>
///     <para>
///         The controls are the classes that do read them, and <c>[ResiliencePolicy]</c>, which is what
///         an action takes.
///     </para>
/// </remarks>
public sealed class AResilienceAttributeNothingReadsIsReportedTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;
        using Pragmatic.Resilience.Attributes;
        using Pragmatic.Result;

        """;

    private static string Action(string attribute) => Usings + $$"""
        namespace TestApp.Payments;

        [DomainAction]
        {{attribute}}
        public partial class ChargeCard : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("ok"));
        }
        """;

    private static string Job(string attribute) => Usings + $$"""
        namespace TestApp.Payments;

        [Job]
        {{attribute}}
        public partial class SettleBatch : IJob
        {
            public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
        }
        """;

    private static string Handler(string attribute) => Usings + $$"""
        namespace TestApp.Payments;

        public sealed record CardCharged(int Id);

        [MessageHandler]
        {{attribute}}
        public partial class NotifyOnCharge : IMessageHandler<CardCharged>
        {
            public Task HandleAsync(CardCharged message, MessageContext context, CancellationToken ct = default)
                => Task.CompletedTask;
        }
        """;

    [Fact]
    public void RetryOnADomainAction_IsReported()
    {
        var source = Action("[Retry(MaxAttempts = 3)]");
        var diagnostic = GeneratorTestHelper.GetGeneratorDiagnostics(Run(source), "PRAG0464")
            .Should().ContainSingle().Which;

        diagnostic.GetMessage().Should().Contain("[Retry]").And.Contain("ChargeCard").And.Contain("[ResiliencePolicy");
        source.Split('\n')[diagnostic.Location.GetLineSpan().StartLinePosition.Line]
            .Should().Contain("[Retry", "it is reported where the attribute is written");
    }

    [Theory]
    [InlineData("[Timeout(TimeoutSeconds = 5)]")]
    [InlineData("[CircuitBreaker]")]
    public void TheOtherTwoOnADomainAction_AreReported(string attribute)
        => GeneratorTestHelper.HasDiagnostic(Run(Action(attribute)), "PRAG0464").Should().BeTrue();

    /// <summary>A job reads <c>[Retry]</c> and <c>[Timeout]</c>.</summary>
    [Theory]
    [InlineData("[Retry(MaxAttempts = 3)]")]
    [InlineData("[Timeout(TimeoutSeconds = 5)]")]
    public void OnAJob_RetryAndTimeoutAreRead(string attribute)
        => GeneratorTestHelper.HasDiagnostic(Run(Job(attribute)), "PRAG0464").Should().BeFalse();

    /// <summary>
    ///     …and not <c>[CircuitBreaker]</c>: no job engine reads it, so on a job it is the same silence.
    /// </summary>
    [Fact]
    public void CircuitBreakerOnAJob_IsReported()
        => GeneratorTestHelper.HasDiagnostic(Run(Job("[CircuitBreaker]")), "PRAG0464").Should().BeTrue();

    /// <summary>A message handler reads all three.</summary>
    [Theory]
    [InlineData("[Retry(MaxAttempts = 3)]")]
    [InlineData("[Timeout(TimeoutSeconds = 5)]")]
    [InlineData("[CircuitBreaker]")]
    public void OnAMessageHandler_AllThreeAreRead(string attribute)
        => GeneratorTestHelper.HasDiagnostic(Run(Handler(attribute)), "PRAG0464").Should().BeFalse();

    /// <summary>What an action takes instead is not reported.</summary>
    [Fact]
    public void AResiliencePolicyOnADomainAction_IsNotReported()
        => GeneratorTestHelper.HasDiagnostic(Run(Action("[ResiliencePolicy(\"payments\")]")), "PRAG0464").Should().BeFalse();

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GeneratorTestHelper.FromType<global::Pragmatic.Resilience.Attributes.RetryAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.DomainActionAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Jobs.Attributes.JobAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Messaging.Attributes.MessageHandlerAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>());
}
