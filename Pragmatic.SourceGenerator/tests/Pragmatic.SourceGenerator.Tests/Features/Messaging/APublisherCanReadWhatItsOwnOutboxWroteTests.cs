using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     An assembly that declares domain events gets a message-type registry, whether or not
///     it handles anything.
/// </summary>
/// <remarks>
///     <para>
///         Without a registry keyed on the events, a service that only <b>publishes</b> — no
///         <c>[MessageHandler]</c> — has the outbox table, the capture interceptor, the delivery pump,
///         and nothing able to read what it has written. <c>OutboxDeliveryService</c> resolves every row
///         through the registered <c>IMessageTypeRegistry</c> instances and dead-letters what none of
///         them recognises, so the publisher's own event would be an "unknown message type" <em>to the
///         publisher</em> and every row would drain into the dead-letter store with no handler anywhere
///         having run. ⚠️ Dead-lettered, not retried.
///     </para>
///     <para>
///         <b>Why one process hides it</b>: an application that publishes and consumes in one process
///         has a registry built from its handlers that happens to know the very types its outbox
///         carries. Split the halves into two services — which is what an outbox on a broker is for —
///         and the publisher is left with a table it cannot read.
///     </para>
///     <para>
///         The set is the one <c>AsyncApiFeature</c> also collects: the
///         domain events <b>declared in this compilation</b>. Those are what an entity here can raise
///         and therefore what this service's outbox can carry. ⚠️ Nothing is invented for an assembly
///         that declares no event: the control below is what keeps "generate a registry" from becoming
///         "generate an empty registry everywhere", which would register a service that answers null to
///         everything and make the dead-letter diagnosis even harder to read.
///     </para>
/// </remarks>
public class APublisherCanReadWhatItsOwnOutboxWroteTests
{
    /// <summary>
    ///     The framework types the generator looks for, in the namespaces it looks for them in.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>HasMessaging</c> is probed on <c>Pragmatic.Messaging.Attributes.MessageHandlerAttribute</c>
    ///     and <b>not</b> on <c>IMessageBus</c>, whatever the remark in
    ///     <c>MessagingShapeDiagnosticsTests</c> says — that comment is stale, and it cost a round here:
    ///     with only the bus stubbed, the flag was false, the registry pipeline returned at its first
    ///     line, and nothing was emitted and nothing reported. Read the detector, not the comment.
    /// </remarks>
    private const string Stubs = """
        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }
        }

        namespace Pragmatic.Messaging.Entities
        {
            public interface IMessageTypeRegistry
            {
                object? Deserialize(string fullyQualifiedTypeName, string json);
            }
        }

        namespace Pragmatic.Events
        {
            public interface IDomainEvent { }
            public interface IIntegrationEvent : IDomainEvent { }
        }
        """;

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + "\n" + body, []);

    private static string? RegistryOf(SourceGenRunResult result)
    {
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        return generated.TryGetValue("_Infra.Messaging.TypeRegistry.g.cs", out var source) ? source : null;
    }

    /// <summary>The setpoint: a publisher-only assembly can resolve the event it publishes.</summary>
    [Fact]
    public void AnAssemblyWithAnEventAndNoHandler_GetsARegistryThatResolvesIt()
    {
        var result = Run("""
            namespace Casework.Intake.Events
            {
                public sealed record VerificationRequested(System.Guid CaseId) : Pragmatic.Events.IIntegrationEvent;
            }
            """);

        var registry = RegistryOf(result);

        registry.Should().NotBeNull(
            "the assembly that declares the event is the one that can deserialize it, and a publisher "
            + "has no handler to derive the type from");
        registry.Should().Contain("\"Casework.Intake.Events.VerificationRequested\" =>",
            "the case label is the key the outbox row carries — Type.FullName, with no global:: prefix");
    }

    /// <summary>
    ///     Control — an assembly that declares no event gets no registry. An empty one would register a
    ///     service that answers null to everything, which is worse than none: it makes the dead-letter
    ///     line say "no registry recognised this" while a registry is in fact registered.
    /// </summary>
    [Fact]
    public void AnAssemblyWithNeitherEventNorHandler_GetsNoRegistry()
    {
        var result = Run("""
            namespace Casework.Intake
            {
                public sealed class NotAnEvent
                {
                    public string Name { get; set; } = "";
                }
            }
            """);

        RegistryOf(result).Should().BeNull();
    }

    /// <summary>
    ///     A plain domain event counts too, not only an integration event: the outbox captures whatever
    ///     a tracked entity raised, and whether it is <em>published</em> is a separate question from
    ///     whether the pump can read the row.
    /// </summary>
    [Fact]
    public void APlainDomainEvent_IsAlsoInTheRegistry()
    {
        var result = Run("""
            namespace Casework.Intake.Events
            {
                public sealed record CaseDecided(System.Guid CaseId) : Pragmatic.Events.IDomainEvent;
            }
            """);

        RegistryOf(result).Should().Contain("\"Casework.Intake.Events.CaseDecided\" =>");
    }

    /// <summary>
    ///     And the registration the host calls has to exist for such an assembly: the registry is
    ///     <c>internal</c>, so only its own assembly can register it, and the host's composition calls
    ///     what the assembly declares.
    /// </summary>
    [Fact]
    public void AnAssemblyWithAnEventAndNoHandler_AlsoGetsTheRegistrationThatRegistersIt()
    {
        var result = Run("""
            namespace Casework.Intake.Events
            {
                public sealed record VerificationRequested(System.Guid CaseId) : Pragmatic.Events.IIntegrationEvent;
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Should().ContainKey("_Infra.Messaging.Registration.g.cs",
            "a registry nobody registers is a class that compiles and does nothing");
        generated["_Infra.Messaging.Registration.g.cs"].Should().Contain(
            "IMessageTypeRegistry, global::Pragmatic.Messaging.Generated.PragmaticMessageTypeRegistry",
            "registered as an enumerable: every assembly contributes its own, and the pump tries each");
    }

    /// <summary>
    ///     And the host has to be told to call it. The registration is only reached because the assembly
    ///     declares it in <c>[assembly: PragmaticMetadata(MessageHandlers, …)]</c>, which the generated
    ///     host already walks for every discovered assembly — so a publisher needs no new channel, only
    ///     to appear in the one that exists.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The namespace in the metadata and the namespace of the registration class have to be the
    ///     same string, and for a handler-less assembly neither could be derived from a handler. Both
    ///     now read the assembly name off a declared event; the legacy fallback is a <b>fixed</b>
    ///     namespace, which two publisher-only assemblies in one host would collide on (CS0433).
    /// </remarks>
    [Fact]
    public void AnAssemblyWithAnEventAndNoHandler_DeclaresItsRegistrationToTheHost()
    {
        var result = Run("""
            namespace Casework.Intake.Events
            {
                public sealed record VerificationRequested(System.Guid CaseId) : Pragmatic.Events.IIntegrationEvent;
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Should().ContainKey("_Metadata.MessageHandlers.g.cs");

        var metadata = generated["_Metadata.MessageHandlers.g.cs"];
        var registration = generated["_Infra.Messaging.Registration.g.cs"];

        metadata.Should().Contain("MetadataCategory.MessageHandlers",
            "the category the generated host already walks, calling every registration it finds");
        metadata.Should().Contain("\"handlerCount\": 0",
            "nothing is invented: this assembly handles nothing and says so, and is called for its "
            + "registry alone");

        // The one string that has to agree between the two files, read out of each rather than assumed.
        // ⚠️ The namespace is named after the ASSEMBLY, which in this harness is the synthetic
        // compilation's name and not the event's namespace — a first version of this assertion confused
        // the two and failed for that reason alone.
        var declaredClass = Between(metadata, "\"registrationMethod\": \"", "\"");
        var declaredNamespace = declaredClass.Substring(0, declaredClass.LastIndexOf('.'));
        declaredNamespace = declaredNamespace.Substring(0, declaredNamespace.LastIndexOf('.'));

        registration.Should().Contain($"namespace {declaredNamespace};",
            "the host calls the class the metadata names, so the two must be the same namespace");
        declaredNamespace.Should().NotBe("Pragmatic.Messaging.Generated",
            "it is named after the declaring assembly and not the fixed fallback, which two "
            + "publisher-only assemblies in one host would collide on (CS0433)");
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        return source.Substring(from, to - from);
    }
}
