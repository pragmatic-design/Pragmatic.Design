using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry.Dtos;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     A customer read back is written by the generated UTF-8 writer of <see cref="CustomerDto" />, through the
///     options the generated entry point gave this host, and the bytes are the serializer's.
/// </summary>
/// <remarks>The control, a host whose options changed, is <see cref="AnsweringThroughTheSerializerWhenTheOptionsChanged" />.</remarks>
public sealed class AnsweringThroughTheGeneratedWriter(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task ACustomerReadBack_IsWrittenByItsGeneratedWriter_AsTheSerializerWouldWriteIt()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());
        var created = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", new
        {
            // Text the two encoders write differently: the host's leaves it as it is.
            name = "Rossi & Figli <S.r.l.> — Forlì",
            vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            email = "billing@customer.test",
            preferredCulture = "it-IT",
            paymentTermsDays = 30,
            addressStreet = "Via Roma 1",
            addressPostCode = "20121",
            addressCity = "Milano",
            addressCountry = "IT",
        }));
        var id = created.GetProperty("id").GetGuid();

        using var writers = new WritersObserved();
        var response = await accountant.GetAsync($"api/customers/{id}");
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        writers.Seen.Should().Contain("generated").And.NotContain("serializer");

        var options = HostOptions();
        var value = JsonSerializer.Deserialize<CustomerDto>(body, options)!;
        body.Should().Be(JsonSerializer.Serialize(value, options),
            "the generated writer writes the bytes the serializer writes under the host's own options");
        value.Name.Should().Be("Rossi & Figli <S.r.l.> — Forlì");
    }

    /// <summary>
    ///     The host's options are the ones the generated entry point wrote, and none of the converters the framework
    ///     registered in them (Internationalization's, for <c>Money</c>) claims a type a customer is written with.
    /// </summary>
    [Fact]
    public void TheHostsOptions_AdmitAWriterOfACustomer()
        => GeneratedJsonDefaults.AllowGeneratedWriters(HostOptions(), new GeneratedJsonShape(
                [typeof(CustomerDto), typeof(Guid), typeof(string), typeof(int)], [], [], needsInfrastructureExclusion: false))
            .Should().BeTrue();

    /// <summary>
    ///     The control: an invoice carries <c>Money</c>, which Internationalization's converter writes, so the host
    ///     does not admit a writer of it and the serializer answers.
    /// </summary>
    [Fact]
    public void TheHostsOptions_RefuseAWriterOfMoney()
        => GeneratedJsonDefaults.AllowGeneratedWriters(HostOptions(), new GeneratedJsonShape(
                [typeof(Invoicing.Billing.Dtos.InvoiceDto), typeof(Pragmatic.Internationalization.Types.Money)], [], [], needsInfrastructureExclusion: false))
            .Should().BeFalse();

    private JsonSerializerOptions HostOptions()
        => Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
}
