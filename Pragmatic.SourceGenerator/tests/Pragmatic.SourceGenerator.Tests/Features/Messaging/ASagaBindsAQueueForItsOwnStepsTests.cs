using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     A saga's registration declares a <b>transport subscription</b> for every message its steps
///     handle, not only a local handler for it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A local <c>IMessageHandler&lt;T&gt;</c> answers a message the process <em>already
///         has</em>. With only the handler declared, nothing binds a queue, so a step whose message
///         arrives over a broker never runs — the <b>start</b> step included, which means the saga never
///         begins and leaves nothing behind to explain why: no row in <c>__SagaInstances</c>.
///     </para>
///     <para>
///         ⚠️⚠️ <b>The symptom points at the wrong cause.</b> Reading the subscription name suggests
///         two services sharing a queue — a separate defect. The generated file is what tells them
///         apart: a saga missing this subscription registers its other messages and <b>no</b>
///         subscription for its start message, so no service declares that queue at all.
///     </para>
///     <para>
///         A <c>[MessageHandler]</c> elsewhere in the module registers the same marker for a type its
///         saga also handles. That is harmless: <c>TransportSubscriptionBinder</c> deduplicates by
///         message type and subscribes once, then dispatches in process to every handler of it.
///     </para>
/// </remarks>
public class ASagaBindsAQueueForItsOwnStepsTests
{
    private static SagaModel Saga(string assembly, string ns, params string[] eventFqns)
    {
        var steps = eventFqns.Select((fqn, i) => new SagaStepModel
        {
            MethodName = "When" + fqn.Substring(fqn.LastIndexOf('.') + 1),
            EventTypeFqn = fqn,
            EventTypeShortName = fqn.Substring(fqn.LastIndexOf('.') + 1),
            IsStart = i == 0,
            NextState = "Done",
        }).ToImmutableArray();

        return new SagaModel
        {
            Namespace = ns,
            TypeName = "CaseProcessSaga",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            AssemblyName = assembly,
            ImplementsISaga = true,
            StateTypeFqn = "global::Casework.Intake.Enums.CaseProcess",
            StateTypeShortName = "CaseProcess",
            StateValues = ImmutableArray.Create("Started", "Done"),
            StartStep = steps[0],
            Steps = steps,
        };
    }

    private static string Render(SagaModel saga)
        => new SagaRegistrationTemplate(ImmutableArray.Create(saga), hasEfCore: false).RenderOutput().Text;

    /// <summary>The setpoint: the start step's message gets a queue of its own.</summary>
    [Fact]
    public void TheStartStepsMessage_GetsATransportSubscription()
    {
        var source = Render(Saga(
            "Casework.Intake",
            "Casework.Intake.Infrastructure.Sagas",
            "global::Casework.Intake.Events.VerificationRequested"));

        source.Should().Contain(
            "AddMessageSubscription<global::Casework.Intake.Events.VerificationRequested>(services, \"intake\")",
            "a local handler answers a message the process already has; a queue is what brings it");
    }

    /// <summary>⚠️ The control: the local handler is still registered.</summary>
    /// <remarks>
    ///     "It subscribes" is satisfied by subscribing and then having nothing to dispatch to. Both
    ///     halves are the mechanism: the queue brings the message, the handler drives the orchestrator.
    /// </remarks>
    [Fact]
    public void TheLocalHandler_IsStillRegisteredBesideTheSubscription()
    {
        var source = Render(Saga(
            "Casework.Intake",
            "Casework.Intake.Infrastructure.Sagas",
            "global::Casework.Intake.Events.VerificationRequested"));

        source.Should().Contain(
            "IMessageHandler<global::Casework.Intake.Events.VerificationRequested>",
            "the bridge from the bus to the orchestrator");
    }

    /// <summary>Every step's message, not only the start one.</summary>
    [Fact]
    public void EveryStepsMessage_GetsOne()
    {
        var source = Render(Saga(
            "Casework.Intake",
            "Casework.Intake.Infrastructure.Sagas",
            "global::Casework.Intake.Events.VerificationRequested",
            "global::Casework.Verify.Events.VerificationAnswered"));

        source.Should().Contain("AddMessageSubscription<global::Casework.Intake.Events.VerificationRequested>");
        source.Should().Contain("AddMessageSubscription<global::Casework.Verify.Events.VerificationAnswered>");
    }

    /// <summary>
    ///     ⚠️ The subscriber is the saga's <b>module</b>, and it has to be the one the module's handlers
    ///     use.
    /// </summary>
    /// <remarks>
    ///     Two names for one module would bind two queues to one topic, deliver two copies into one
    ///     process, and run every handler of that type <b>twice</b> — which is worse than the defect
    ///     either fix in this story addresses. Read off the assembly, so a saga kept in a folder called
    ///     <c>Sagas</c> is not a consumer group called <c>"sagas"</c>.
    /// </remarks>
    [Fact]
    public void TheSubscriber_IsTheModuleAndNotTheFolder()
    {
        var source = Render(Saga(
            "Casework.Intake", "Casework.Intake.Sagas", "global::Casework.Intake.Events.VerificationRequested"));

        source.Should().NotContain("(services, \"sagas\")");
        source.Should().Contain("(services, \"intake\")");
    }
}
