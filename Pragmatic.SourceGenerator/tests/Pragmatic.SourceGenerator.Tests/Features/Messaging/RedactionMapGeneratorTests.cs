using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

// The map is not emitted per message type: the Redaction feature emits it from the attributes
// themselves, so these three shapes — a nested type, a positional record parameter, a
// [JsonPropertyName] rename — are exactly what has to keep working.

/// <summary>
///     [NotLogged] → the generated IRedactionMap. Covers the positional-record case explicitly,
///     where the attribute sits on a ParameterSyntax with a [property:] target rather than on a
///     property declaration — the shape a map that reads only property declarations misses.
/// </summary>
public class RedactionMapGeneratorTests
{
    private const string Stubs = """
        // The Redaction feature only emits where the assembly can compile the map — see the
        // activation gate in RedactionFeature. Without these two the generated file is correctly
        // withheld, which is what these tests hit when the map moved out of Messaging.
        namespace Pragmatic.Serialization
        {
            public enum RedactionReason { NotLogged = 0, PersonalData = 1 }
            public readonly record struct RedactedMember(string Name, RedactionReason Reason, string? Category = null);
            public interface IRedactionMap
            {
                bool TryGetRedactedMembers(System.Type type, out System.Collections.Generic.IReadOnlyList<RedactedMember> members);
            }
        }
        namespace Microsoft.Extensions.DependencyInjection.Extensions
        {
            public static class ServiceCollectionDescriptorExtensions { }
        }

        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
            public sealed record MessageContext(string MessageId);
            public interface IMessageHandler<in T>
            {
                int Order => 0;
                System.Threading.Tasks.Task HandleAsync(T message, MessageContext context, System.Threading.CancellationToken ct = default);
            }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            // FeatureDetector probes this FQN for HasMessaging
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }
        }

        namespace Pragmatic
        {
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class NotLoggedAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void NotLogged_OnPositionalRecordParameter_GeneratesRedactionMap()
    {
        var source = Stubs + """

            namespace TestApp
            {
                public sealed record LoginAttempted(string Email, [property: Pragmatic.NotLogged] string Password);

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class LoginHandler : Pragmatic.Messaging.IMessageHandler<LoginAttempted>
                {
                    public System.Threading.Tasks.Task HandleAsync(LoginAttempted message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var map = generated.Keys.Should().Contain(k => k.Contains("RedactionMap"),
            "generated files: {0}", string.Join("; ", generated.Keys)).And.Subject
            .First(k => k.Contains("RedactionMap"));
        generated[map].Should().Contain("GeneratedRedactionMap");
        generated[map].Should().Contain("\"Password\"");
        generated[map].Should().NotContain("\"Email\"", "only [NotLogged] members are redacted");

        var registration = generated.Keys.First(k => k.Contains("Redaction.Registration"));
        generated[registration].Should().Contain("IRedactionMap",
            "the map must actually be registered in DI (lesson: generated-but-never-registered)");
    }

    // The audit serializer redacts per graph NODE, so a [NotLogged] member on a NESTED type needs its
    // own map entry — collecting only the top-level message type would let nested secrets through in
    // cleartext.
    [Fact]
    public void NotLogged_OnNestedType_GeneratesEntryForThatType()
    {
        var source = Stubs + """

            namespace TestApp
            {
                public sealed record CardDetails(string Holder, [property: Pragmatic.NotLogged] string Pan);
                public sealed record Attachment([property: Pragmatic.NotLogged] string Secret);
                public sealed record PaymentReceived(System.Guid Id, CardDetails Card, System.Collections.Generic.List<Attachment> Attachments);

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class PaymentHandler : Pragmatic.Messaging.IMessageHandler<PaymentReceived>
                {
                    public System.Threading.Tasks.Task HandleAsync(PaymentReceived message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var map = generated.Keys.Should().Contain(k => k.Contains("RedactionMap"),
            "a nested [NotLogged] must still produce a map").And.Subject
            .First(k => k.Contains("RedactionMap"));

        generated[map].Should().Contain("TestApp.CardDetails",
            "the nested type needs its own entry — the serializer looks up the current graph node's type");
        generated[map].Should().Contain("\"Pan\"");
        generated[map].Should().Contain("TestApp.Attachment",
            "types reached through a collection must be walked too");
        generated[map].Should().Contain("\"Secret\"");
    }

    // The audit serializer matches on the SERIALIZED name (JsonPropertyInfo.Name), so a [NotLogged]
    // member renamed with [JsonPropertyName] must be listed by its JSON name — emitting the CLR name
    // would let the renamed secret through in cleartext.
    [Fact]
    public void NotLogged_WithJsonPropertyName_EmitsJsonName()
    {
        var source = Stubs + """

            namespace System.Text.Json.Serialization
            {
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
                public sealed class JsonPropertyNameAttribute : System.Attribute
                {
                    public JsonPropertyNameAttribute(string name) { }
                }
            }

            namespace TestApp
            {
                public sealed record PaymentReceived(
                    [property: Pragmatic.NotLogged, System.Text.Json.Serialization.JsonPropertyName("pan")] string CardNumber);

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class PaymentHandler : Pragmatic.Messaging.IMessageHandler<PaymentReceived>
                {
                    public System.Threading.Tasks.Task HandleAsync(PaymentReceived message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var map = generated.Keys.First(k => k.Contains("RedactionMap"));
        generated[map].Should().Contain("\"pan\"", "the map must list the serialized JSON name");
        generated[map].Should().NotContain("\"CardNumber\"", "the CLR name never matches the serialized name");
    }

    [Fact]
    public void NoNotLoggedMembers_StillGeneratesAnEmptyMap()
    {
        var source = Stubs + """

            namespace TestApp
            {
                public sealed record PlainMessage(string Text);

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class PlainHandler : Pragmatic.Messaging.IMessageHandler<PlainMessage>
                {
                    public System.Threading.Tasks.Task HandleAsync(PlainMessage message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        // The map IS emitted, empty, so that an ABSENT map means one thing only — the generator did
        // not run. If "no map" also meant "nothing was declared", it would be ambiguous, and a runtime
        // cannot fail closed on an ambiguity.
        var map = generated.Keys.First(k => k.Contains("RedactionMap"));
        generated[map].Should().Contain("members = [];").And.NotContain("\"Password\"");
    }
}
