using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Showcase.Billing.Events;
using Xunit;

namespace Showcase.Tests.Serialization;

/// <summary>
///     End-to-end proof that the W3 SG-emitted <c>JsonSerializerContext</c> covers this module's
///     real boundary types with the reflection fallback DISABLED — i.e. exactly what a Native AOT host
///     relies on. <see cref="InvoicePaid"/> is a positional <c>record</c> (no parameterless ctor, init-only
///     properties), so this also exercises the <c>[UnsafeAccessor]</c> construction/assignment path.
/// </summary>
public sealed class GeneratedJsonContextTests
{
    private static JsonSerializerOptions AotOptions() =>
        new PragmaticJsonOptions()
            .AddContext(Showcase.Billing.Generated.PragmaticJsonContext.Default)
            .DisableReflectionFallback()
            .Build();

    [Fact]
    public void GeneratedContext_CoversRecordEvent_RoundTripsWithFallbackOff()
    {
        var options = AotOptions();
        var evt = new InvoicePaid(Guid.NewGuid(), Guid.NewGuid(), 129.90m, "EUR", DateTimeOffset.UtcNow);

        var info = (JsonTypeInfo<InvoicePaid>)options.GetTypeInfo(typeof(InvoicePaid));
        var json = JsonSerializer.Serialize(evt, info);
        json.Should().Contain("\"totalAmount\"").And.Contain("\"currency\"");

        var back = JsonSerializer.Deserialize(json, info);
        back.Should().NotBeNull();
        back!.InvoiceId.Should().Be(evt.InvoiceId);
        back.TotalAmount.Should().Be(129.90m);
        back.Currency.Should().Be("EUR");
    }
}
