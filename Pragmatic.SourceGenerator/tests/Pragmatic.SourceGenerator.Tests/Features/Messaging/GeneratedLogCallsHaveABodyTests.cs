using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     Every method the messaging pipeline and the saga orchestrator call is implemented in the file
///     that calls it.
/// </summary>
/// <remarks>
///     <para>
///         Both templates used to declare their log methods as <c>[LoggerMessage] partial void</c> and
///         leave the body to Microsoft's logging generator. That generator does not see the output of
///         another generator, so the body never came, and a <c>partial void</c> without an
///         implementation is legal: the compiler deletes every call to it, arguments included. The
///         handler pipeline and the saga logged nothing, and nothing failed.
///     </para>
///     <para>
///         The check is syntactic and per file on purpose: a body in some other output would come from
///         a generator, and a generator cannot rely on another one having run.
///     </para>
/// </remarks>
public class GeneratedLogCallsHaveABodyTests
{
    [Fact]
    public void HandlerPipeline_DeclaresNoPartialMethodWithoutABody()
    {
        var source = new HandlerPipelineTemplate(FullyFeaturedHandler()).RenderOutput().Text;

        BodilessPartialMethods(source).Should().BeEmpty(
            "a partial void without an implementation is compiled away, and the pipeline's log lines with it");
    }

    [Fact]
    public void SagaOrchestrator_DeclaresNoPartialMethodWithoutABody()
    {
        var source = new SagaOrchestratorTemplate(CompensatingSaga()).RenderOutput().Text;

        BodilessPartialMethods(source).Should().BeEmpty(
            "a partial void without an implementation is compiled away, and the saga's log lines with it");
    }

    /// <summary>
    ///     The control: the detector finds a bodiless partial method when there is one, so an empty
    ///     result above is a finding and not a detector that never looks.
    /// </summary>
    [Fact]
    public void TheDetector_FindsABodilessPartialMethod()
    {
        const string source = """
            partial class Sample
            {
                partial void Declared(string value);
                partial void Implemented(string value);
                partial void Implemented(string value) { }
            }
            """;

        BodilessPartialMethods(source).Should().Equal("Declared");
    }

    /// <summary>The fully-featured pipeline: every conditional log method is emitted.</summary>
    [Fact]
    public void HandlerPipeline_TheSampleEmitsEveryConditionalLogCall()
    {
        var source = new HandlerPipelineTemplate(FullyFeaturedHandler()).RenderOutput().Text;

        source.Should().Contain("LogRetryAttempt(");
        source.Should().Contain("LogCircuitOpen(");
        source.Should().Contain("LogRedeliveryScheduled(");
    }

    private static string[] BodilessPartialMethods(string source)
    {
        var methods = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(m => m.Modifiers.Any(SyntaxKind.PartialKeyword))
            .ToList();

        var implemented = methods
            .Where(m => m.Body is not null || m.ExpressionBody is not null)
            .Select(m => m.Identifier.ValueText)
            .ToHashSet();

        return methods
            .Where(m => m.Body is null && m.ExpressionBody is null)
            .Select(m => m.Identifier.ValueText)
            .Where(name => !implemented.Contains(name))
            .ToArray();
    }

    private static MessageHandlerModel FullyFeaturedHandler() => new()
    {
        Namespace = "MyApp.Handlers",
        TypeName = "OrderPlacedHandler",
        Accessibility = "public",
        TypeKind = "class",
        IsPartial = true,
        MessageTypeFqn = "global::MyApp.Events.OrderPlaced",
        MessageTypeShortName = "OrderPlaced",
        HasRetry = true,
        RetryMaxAttempts = 2,
        RetryStrategy = BackoffStrategyValues.Exponential,
        RetryBaseDelayMs = 100,
        HasCircuitBreaker = true,
        CbFailureThreshold = 3,
        CbBreakDurationSeconds = 30,
        HasRedelivery = true,
        RedeliveryMaxAttempts = 3,
        RedeliveryBaseDelaySeconds = 10,
    };

    private static SagaModel CompensatingSaga() => new()
    {
        Namespace = "MyApp.Ordering",
        TypeName = "OrderSaga",
        Accessibility = "public",
        TypeKind = "class",
        IsPartial = true,
        StateTypeFqn = "global::MyApp.Ordering.OrderState",
        StateTypeShortName = "OrderState",
        StateValues = ImmutableArray.Create("PaymentPending", "Shipping"),
        StartStep = new SagaStepModel
        {
            MethodName = "HandleOrderRequested",
            EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
            EventTypeShortName = "OrderRequested",
            IsStart = true,
            NextState = "PaymentPending",
        },
        Steps = ImmutableArray.Create(
            new SagaStepModel
            {
                MethodName = "HandleOrderRequested",
                EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
                EventTypeShortName = "OrderRequested",
                IsStart = true,
                NextState = "PaymentPending",
            },
            new SagaStepModel
            {
                MethodName = "HandlePaymentReceived",
                EventTypeFqn = "global::MyApp.Ordering.Events.PaymentReceived",
                EventTypeShortName = "PaymentReceived",
                ValidStates = ImmutableArray.Create("PaymentPending"),
                NextState = "Shipping",
                CompensationActionFqn = "global::MyApp.Ordering.Actions.RefundPayment",
            }),
    };
}
