using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     Runs the full unified generator over sources with [PartitionKey] to verify the
///     resolver is emitted for both normal properties and positional record parameters.
/// </summary>
public class PartitionKeyGeneratorTests
{
    // Minimal messaging stubs: FeatureDetector probes IMessageBus for HasMessaging,
    // and the pipeline needs the attributes + IMessageHandler<T>.
    private const string MessagingStubs = """
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
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class PartitionKeyAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void PartitionKey_OnNormalProperty_EmitsResolver()
    {
        var source = MessagingStubs + """

            namespace TestApp
            {
                public sealed class OrderPlaced
                {
                    [Pragmatic.Messaging.Attributes.PartitionKey]
                    public string Region { get; set; } = "";
                }

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class OrderHandler : Pragmatic.Messaging.IMessageHandler<OrderPlaced>
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().Contain(k => k.Contains("PartitionKeys"));
        var resolver = generated.First(kv => kv.Key.Contains("PartitionKeys")).Value;
        resolver.Should().Contain("GeneratedPartitionKeyResolver");
        resolver.Should().Contain("typed.Region");

        var registration = generated.First(kv => kv.Key.Contains("Messaging.Registration")).Value;
        registration.Should().Contain("GeneratedPartitionKeyResolver");
    }

    [Fact]
    public void PartitionKey_OnRecordBodyProperty_EmitsResolver()
    {
        var source = MessagingStubs + """

            namespace TestApp
            {
                public sealed record CatalogSynced(System.Guid CatalogId)
                {
                    [Pragmatic.Messaging.Attributes.PartitionKey]
                    public System.Guid PartitionId => CatalogId;
                }

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class CatalogHandler : Pragmatic.Messaging.IMessageHandler<CatalogSynced>
                {
                    public System.Threading.Tasks.Task HandleAsync(CatalogSynced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().Contain(k => k.Contains("PartitionKeys"));
        var resolver = generated.First(kv => kv.Key.Contains("PartitionKeys")).Value;
        resolver.Should().Contain("typed.PartitionId.ToString()");
    }

    [Fact]
    public void PartitionKey_OnPositionalRecordParameter_EmitsResolver()
    {
        // ⚠️ FAWMN does not surface [property: PartitionKey] on a positional record parameter — not
        // even with the predicate widened to ParameterSyntax and the transform mapping a parameter
        // symbol to its property. That does not mean the key can only be declared in a record body:
        // the collection is a symbol scan, so this case is served where the message is declared, and
        // a handler in the same compilation is incidental — see the publisher-only case below, which
        // has no handler at all.
        var source = MessagingStubs + """

            namespace TestApp
            {
                public sealed record CatalogSynced([property: Pragmatic.Messaging.Attributes.PartitionKey] System.Guid CatalogId);

                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class CatalogHandler : Pragmatic.Messaging.IMessageHandler<CatalogSynced>
                {
                    public System.Threading.Tasks.Task HandleAsync(CatalogSynced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().Contain(k => k.Contains("PartitionKeys"));
        var resolver = generated.First(kv => kv.Key.Contains("PartitionKeys")).Value;
        resolver.Should().Contain("typed.CatalogId.ToString()");

        var registration = generated.First(kv => kv.Key.Contains("Messaging.Registration")).Value;
        registration.Should().Contain("GeneratedPartitionKeyResolver");
    }

    /// <summary>
    ///     The shape a distributed application actually uses: the message is declared in a
    ///     contracts assembly that publishes it and handles nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ This is where the handler-transform fallback could not help, and the reason is the
    ///         topology rather than the attribute: it scans from a handler, so it fires only in the
    ///         assembly that <b>consumes</b> the message, while <c>TransportAwareMessageBus</c> resolves
    ///         <c>IPartitionKeyResolver</c> from the <b>publisher's</b> container. The resolver was
    ///         generated in the one assembly that could not use it.
    ///     </para>
    ///     <para>
    ///         The registration matters as much as the resolver: a resolver nothing registers is the
    ///         same inert declaration in a different file.
    ///     </para>
    /// </remarks>
    [Fact]
    public void PartitionKey_InAPublisherOnlyAssembly_EmitsAndRegistersTheResolver()
    {
        var source = MessagingStubs + """

            namespace TestApp
            {
                public sealed record CatalogSynced([property: Pragmatic.Messaging.Attributes.PartitionKey] System.Guid CatalogId);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().Contain(k => k.Contains("PartitionKeys"));
        generated.First(kv => kv.Key.Contains("PartitionKeys")).Value
            .Should().Contain("typed.CatalogId.ToString()");

        generated.Keys.Should().Contain(k => k.Contains("Messaging.Registration"));
        generated.First(kv => kv.Key.Contains("Messaging.Registration")).Value
            .Should().Contain("GeneratedPartitionKeyResolver");
    }

    /// <summary>
    ///     The control: without the attribute nothing is emitted, so "a resolver exists" is not
    ///     satisfied by a generator that emits one for every message type it sees.
    /// </summary>
    [Fact]
    public void WithoutPartitionKey_InAPublisherOnlyAssembly_NoResolverIsEmitted()
    {
        var source = MessagingStubs + """

            namespace TestApp
            {
                public sealed record CatalogSynced(System.Guid CatalogId);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().NotContain(k => k.Contains("PartitionKeys"));
    }
}
