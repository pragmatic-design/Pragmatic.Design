using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     PRAG0836 — <c>[PublicEvent]</c> marks a domain event as published. On a type that is not a domain
///     event it is accepted, documented and inert, and says so.
/// </summary>
/// <remarks>
///     <para>
///         A record marked <c>[PublicEvent]</c> and published to RabbitMQ, without implementing
///         <c>IDomainEvent</c>, produces <b>no AsyncAPI document at all</b>. Every consumer of the
///         marker starts from <c>IDomainEvent</c> — <c>AsyncApiFeature</c> catalogues types that
///         implement it, and the writer-side outbox captures events raised by tracked
///         <c>IHasDomainEvents</c> entities — so without the diagnostic the published contract of a
///         service would be the one kind of event that silently cannot appear in the document meant to
///         publish it.
///     </para>
///     <para>
///         The model stands: an integration event <em>is</em> a domain event that is also published
///         (<c>IIntegrationEvent : IDomainEvent</c>). What the framework owes is to say so where the
///         shape is wrong, which is this diagnostic.
///     </para>
///     <para>
///         ⚠️ The controls are the point. Reporting whenever the interface is absent would also fire on
///         the recommended shape — a record implementing <c>IIntegrationEvent</c>, which
///         carries <c>IDomainEvent</c> through its base — and a diagnostic that fires on the
///         recommended form is worse than none.
///     </para>
/// </remarks>
public class APublicEventThatIsNotADomainEventTests
{
    /// <summary>
    ///     The event types as the framework declares them, in the namespace it declares them in:
    ///     <c>[PublicEvent]</c> is <c>Pragmatic.Events.PublicEventAttribute</c> and lives in
    ///     Pragmatic.Abstractions, with no <c>.Attributes</c> segment — unlike its neighbours.
    /// </summary>
    private const string Stubs = """
        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
        }

        namespace Pragmatic.Events
        {
            public interface IDomainEvent { }
            public interface IIntegrationEvent : IDomainEvent { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PublicEventAttribute : System.Attribute { }
        }
        """;

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + "\n" + body, []);

    /// <summary>The setpoint: the silence this story exists to close.</summary>
    [Fact]
    public void PublicEvent_OnARecordThatImplementsNothing_ReportsPRAG0836()
    {
        var result = Run("""
            namespace Contracts
            {
                [Pragmatic.Events.PublicEvent]
                public sealed record VerificationRequested(System.Guid CaseId);
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0836").Should().BeTrue(
            "a marker nothing reads has to say so, and a record is the shape a published contract "
            + "actually takes");
    }

    /// <summary>
    ///     Control — the form the owner's decision blesses stays silent: a domain event marked public.
    /// </summary>
    [Fact]
    public void PublicEvent_OnADomainEvent_ReportsNothing()
    {
        var result = Run("""
            namespace Contracts
            {
                [Pragmatic.Events.PublicEvent]
                public sealed record VerificationRequested(System.Guid CaseId) : Pragmatic.Events.IDomainEvent;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0836").Should().BeFalse();
    }

    /// <summary>
    ///     Control — and so does the other blessed form: <c>IIntegrationEvent</c> carries
    ///     <c>IDomainEvent</c> through its base, so the check has to look at the whole interface set and
    ///     not at what the declaration happens to name.
    /// </summary>
    [Fact]
    public void PublicEvent_OnAnIntegrationEvent_ReportsNothing()
    {
        var result = Run("""
            namespace Contracts
            {
                [Pragmatic.Events.PublicEvent]
                public sealed record VerificationRequested(System.Guid CaseId) : Pragmatic.Events.IIntegrationEvent;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0836").Should().BeFalse(
            "IIntegrationEvent : IDomainEvent, so this is a domain event that is also published — the "
            + "recommended shape, and a diagnostic that fired here would be worse than none");
    }

    /// <summary>
    ///     Control — nothing is reported about a type nobody marked. The diagnostic is about the
    ///     marker, not about records that are not events.
    /// </summary>
    [Fact]
    public void ARecordWithNoMarker_ReportsNothing()
    {
        var result = Run("""
            namespace Contracts
            {
                public sealed record JustAPayload(System.Guid CaseId);
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0836").Should().BeFalse();
    }

    /// <summary>
    ///     Control — the document still catalogues a domain event. The diagnostic is an addition, and
    ///     the AsyncAPI half of the marker keeps working: this is the assertion that says the diagnostic
    ///     does not buy a message by losing the output.
    /// </summary>
    [Fact]
    public void ADomainEventMarkedPublic_IsStillInTheGeneratedAsyncApi()
    {
        var result = Run("""
            namespace Contracts
            {
                [Pragmatic.Events.PublicEvent]
                public sealed record VerificationRequested(System.Guid CaseId) : Pragmatic.Events.IDomainEvent;
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var asyncApi = generated.FirstOrDefault(f => f.Key.Contains("AsyncApi", StringComparison.Ordinal));

        asyncApi.Value.Should().NotBeNull("the marked domain event is what the document exists to publish");
        asyncApi.Value.Should().Contain("VerificationRequested");
    }
}
