using System;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     The three docs-gen features (Glossary / Architecture / AsyncApi) are registered WITHOUT a
///     <c>DetectedFeatures</c> gate, so they run on every compilation. The template-level tests build
///     their models by hand and therefore prove nothing about the pipeline that feeds them: whether the
///     attribute/interface probes actually match, whether the emitted constant compiles, and whether an
///     empty domain correctly produces no file at all.
///     These tests run the FULL <see cref="PragmaticSourceGenerator" /> from source.
/// </summary>
public class GlossaryPipelineTests
{
    private const string EntityShim = """
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }
        """;

    private const string ComposeShim = """
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IncludeAttribute<T1> : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IncludeAttribute<T1, T2> : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class RemoteBoundaryAttribute<T1> : System.Attribute { }
        }
        """;

    private const string EventShim = """
        namespace Pragmatic.Events
        {
            public interface IDomainEvent { System.DateTimeOffset OccurredAt { get; } }
            public interface IIntegrationEvent : IDomainEvent { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PublicEventAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ObsoleteEventAttribute : System.Attribute
            {
                public ObsoleteEventAttribute(string? removeBy = null) { }
            }
        }
        """;

    /// <summary>
    ///     Pulls the payload out of the generated <c>public const string X = @"…";</c> verbatim literal
    ///     and undoes the <c>""</c> escaping, giving back the Markdown / Mermaid / JSON as emitted.
    /// </summary>
    private static string ExtractVerbatimConstant(string generatedSource)
    {
        var start = generatedSource.IndexOf("@\"", StringComparison.Ordinal);
        var end = generatedSource.LastIndexOf("\";", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the artifact must embed its payload in a verbatim literal");
        end.Should().BeGreaterThan(start);
        return generatedSource.Substring(start + 2, end - start - 2).Replace("\"\"", "\"");
    }

    /// <summary>The JSON-schema property names of one message's payload.</summary>
    private static List<string> PayloadPropertyNames(System.Text.Json.JsonElement messages, string messageKey)
        => messages.GetProperty(messageKey).GetProperty("payload").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToList();

    [Fact]
    public void Glossary_FromEntitySource_EmitsMarkdownConstantGroupedByNamespace()
    {
        var source = EntityShim + """

            namespace Sales
            {
                /// <summary>A customer order.</summary>
                [Pragmatic.Persistence.Entity.Entity]
                public class Order { public System.Guid Id { get; set; } }

                [Pragmatic.Persistence.Entity.Entity]
                public class Customer { public System.Guid Id { get; set; } }
            }

            namespace Billing
            {
                /// <summary>A bill to settle.</summary>
                [Pragmatic.Persistence.Entity.Entity]
                public class Invoice { public System.Guid Id { get; set; } }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey("_Infra.Glossary.Generated.g.cs");

        var markdown = ExtractVerbatimConstant(generated["_Infra.Glossary.Generated.g.cs"]);
        markdown.Should().StartWith("# Glossary");
        markdown.Should().Contain("## Billing");
        markdown.Should().Contain("## Sales");
        markdown.Should().Contain("- **Order** — A customer order.");
        markdown.Should().Contain("- **Invoice** — A bill to settle.");
        markdown.Should().Contain("- **Customer**", "an entity without an XML summary is still a domain term");
        markdown.Should().NotContain("- **Customer** —");
    }

    [Fact]
    public void Glossary_GenericEntityAttribute_IsAlsoCollected()
    {
        // Both EntityAttribute and EntityAttribute`1 feed the same glossary; the boundary-typed form
        // is what real code uses, so a probe that only matched the non-generic one would emit nothing.
        var source = EntityShim + """

            namespace Sales
            {
                public sealed class SalesBoundary { }

                /// <summary>A customer order.</summary>
                [Pragmatic.Persistence.Entity.Entity]
                public class Order { public System.Guid Id { get; set; } }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey("_Infra.Glossary.Generated.g.cs");
        ExtractVerbatimConstant(generated["_Infra.Glossary.Generated.g.cs"])
            .Should().Contain("- **Order** — A customer order.");
    }

    [Fact]
    public void Architecture_FromIncludeAndRemoteBoundary_EmitsMermaidDiagram()
    {
        var source = ComposeShim + """

            namespace App
            {
                public sealed class CatalogModule { }
                public sealed class BillingModule { }

                [Pragmatic.Composition.Attributes.Include<CatalogModule>]
                [Pragmatic.Composition.Attributes.RemoteBoundary<BillingModule>]
                public sealed class Startup { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey("_Infra.Architecture.Generated.g.cs");

        var mermaid = ExtractVerbatimConstant(generated["_Infra.Architecture.Generated.g.cs"]);
        mermaid.Should().StartWith("flowchart TD");
        mermaid.Should().Contain("CatalogModule[CatalogModule]");
        mermaid.Should().Contain("BillingModule[[BillingModule (remote)]]");
        mermaid.Should().Contain("Host --> CatalogModule");
        mermaid.Should().Contain("Host --> BillingModule");
    }

    [Fact]
    public void AsyncApi_FromDomainEvents_EmitsValidAsyncApi30Document()
    {
        var source = EventShim + """

            namespace Sales
            {
                public sealed record OrderPlaced(System.Guid OrderId, decimal Total, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;

                public sealed record OrderExported(System.Guid OrderId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IIntegrationEvent;

                // No base list → not an event, and the syntax pre-filter must not pick it up.
                public sealed record NotAnEvent(System.Guid Id);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey("_Infra.AsyncApi.Generated.g.cs");

        var json = ExtractVerbatimConstant(generated["_Infra.AsyncApi.Generated.g.cs"]);
        JsonValidator.IsValid(json).Should().BeTrue("the emitted AsyncAPI document is consumed as JSON");

        json.Should().Contain("\"asyncapi\": \"3.0.0\"");
        json.Should().Contain("\"Sales.OrderPlaced\"");
        json.Should().Contain("#/components/messages/Sales.OrderPlaced");
        json.Should().Contain("\"Sales.OrderExported\"");
        json.Should().NotContain("NotAnEvent");

        // IIntegrationEvent → public contract; a plain IDomainEvent is internal.
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("components").GetProperty("messages");
        messages.GetProperty("Sales.OrderExported").GetProperty("x-pragmatic-public").GetBoolean().Should().BeTrue();
        messages.GetProperty("Sales.OrderPlaced").GetProperty("x-pragmatic-public").GetBoolean().Should().BeFalse();

        // Payload schema is derived from the record's properties, keyed by the name each one SERIALIZES
        // to (camelCase, per PragmaticJsonOptions) and with JSON types mapped.
        var payload = messages.GetProperty("Sales.OrderPlaced").GetProperty("payload").GetProperty("properties");
        payload.GetProperty("orderId").GetProperty("type").GetString().Should().Be("string");
        payload.GetProperty("total").GetProperty("type").GetString().Should().Be("number");
        payload.GetProperty("occurredAt").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void AsyncApi_TwoEventsWithTheSameSimpleName_BothKeepTheirOwnContract()
    {
        // The identity of an event is its full name. Keying the document on the simple name
        // makes one of two homonymous events silently disappear from the contract.
        var source = EventShim + """

            namespace Billing
            {
                public sealed record StatusChanged(System.Guid InvoiceId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;
            }

            namespace Booking
            {
                public sealed record StatusChanged(System.Guid ReservationId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;
            }

            namespace Sales
            {
                public sealed record OrderPlaced(System.Guid OrderId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var json = ExtractVerbatimConstant(
            GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)["_Infra.AsyncApi.Generated.g.cs"]);

        var doc = System.Text.Json.JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("components").GetProperty("messages");
        var names = messages.EnumerateObject().Select(p => p.Name).ToList();

        names.Should().HaveCount(3, "no event may be dropped from the contract");
        names.Should().Contain("Billing.StatusChanged");
        names.Should().Contain("Booking.StatusChanged");
        names.Should().Contain("Sales.OrderPlaced", "every key is fully qualified, so adding an event elsewhere never renames this one");

        // The channels must be disambiguated the same way, and reference the matching message. ⚠️ The
        // disambiguation is the channel's KEY, which is what this test is about; the address is the
        // topic the transport uses and is deliberately NOT unique per event — two events of one
        // boundary share it. Asserting the key is what keeps this test about identity.
        var channels = doc.RootElement.GetProperty("channels");
        channels.EnumerateObject().Select(c => c.Name).Should().Contain("Billing.StatusChanged");
        channels.GetProperty("Billing.StatusChanged").GetProperty("address").GetString()
            .Should().Be("billing.events");
        json.Should().Contain("#/components/messages/Booking.StatusChanged");

        // The payloads must not be crossed over either — each keeps its own properties. Compared
        // case-insensitively so this test stays about identity, not about the JSON naming policy.
        PayloadPropertyNames(messages, "Billing.StatusChanged")
            .Should().Contain(n => n.Equals("invoiceId", StringComparison.OrdinalIgnoreCase));
        PayloadPropertyNames(messages, "Booking.StatusChanged")
            .Should().Contain(n => n.Equals("reservationId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AsyncApi_SchemaUsesTheJsonNamesActuallySerialized()
    {
        // The shared PragmaticJsonOptions serialize camelCase and honour [JsonPropertyName].
        // A schema built from the CLR member names describes a payload nobody ever sends.
        var source = EventShim + """

            namespace Sales
            {
                public sealed record OrderPlaced(
                    System.Guid OrderId,
                    [property: System.Text.Json.Serialization.JsonPropertyName("total_amount")] decimal Total,
                    System.DateTimeOffset OccurredAt) : Pragmatic.Events.IDomainEvent;
            }
            """;

        // The real [JsonPropertyName] from System.Text.Json — a hand-written shim would prove only that
        // the probe matches a name we invented ourselves.
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source,
            [GeneratorTestHelper.FromType<System.Text.Json.Serialization.JsonPropertyNameAttribute>()]);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var json = ExtractVerbatimConstant(
            GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)["_Infra.AsyncApi.Generated.g.cs"]);

        var doc = System.Text.Json.JsonDocument.Parse(json);
        var properties = doc.RootElement.GetProperty("components").GetProperty("messages")
            .GetProperty("Sales.OrderPlaced").GetProperty("payload").GetProperty("properties");

        var names = properties.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().BeEquivalentTo(["orderId", "total_amount", "occurredAt"]);
        properties.GetProperty("total_amount").GetProperty("type").GetString().Should().Be("number");
        properties.GetProperty("orderId").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void AsyncApi_MarkerAttributesAreMatchedByIdentityNotByName()
    {
        // [PublicEvent]/[ObsoleteEvent] are Pragmatic.Events markers. An application attribute
        // that merely shares the simple name must not rewrite the published contract.
        var source = EventShim + """

            namespace MyApp.Annotations
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class PublicEventAttribute : System.Attribute { }

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class ObsoleteEventAttribute : System.Attribute { }
            }

            namespace Sales
            {
                // Homonymous, unrelated attributes: this stays an internal, non-obsolete domain event.
                [MyApp.Annotations.PublicEvent]
                [MyApp.Annotations.ObsoleteEvent]
                public sealed record OrderPlaced(System.Guid OrderId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;

                // The real markers still work.
                [Pragmatic.Events.PublicEvent]
                [Pragmatic.Events.ObsoleteEvent]
                public sealed record OrderShipped(System.Guid OrderId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var json = ExtractVerbatimConstant(
            GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)["_Infra.AsyncApi.Generated.g.cs"]);

        var messages = System.Text.Json.JsonDocument.Parse(json)
            .RootElement.GetProperty("components").GetProperty("messages");

        var foreign = messages.GetProperty("Sales.OrderPlaced");
        foreign.GetProperty("x-pragmatic-public").GetBoolean().Should().BeFalse();
        foreign.TryGetProperty("x-pragmatic-obsolete", out _).Should().BeFalse();

        var real = messages.GetProperty("Sales.OrderShipped");
        real.GetProperty("x-pragmatic-public").GetBoolean().Should().BeTrue();
        real.GetProperty("x-pragmatic-obsolete").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DocsGeneration_NoEntitiesModulesOrEvents_EmitsNothing()
    {
        // The marker types are all present, but nothing uses them. Validate() returns false for each
        // template and — since an empty artifact is not added to the compilation — not one of the
        // three files may appear.
        var source = EntityShim + ComposeShim + EventShim + """

            namespace App
            {
                public sealed class PlainService { public int Value { get; set; } }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();

        var hints = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys;
        hints.Should().NotContain("_Infra.Glossary.Generated.g.cs");
        hints.Should().NotContain("_Infra.Architecture.Generated.g.cs");
        hints.Should().NotContain("_Infra.AsyncApi.Generated.g.cs");
    }
}
