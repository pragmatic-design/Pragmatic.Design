using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The control of <see cref="AnsweringThroughTheGeneratedWriter" />: a host that changes its response options
///     after the entry point — a converter added in its own configuration — answers through the serializer.
/// </summary>
public sealed class AnsweringThroughTheSerializerWhenTheOptionsChanged(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task ACustomerReadBack_IsWrittenByTheSerializer()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());
        var created = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", new
        {
            name = "Bianchi S.p.A.",
            vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            email = "billing@customer.test",
            preferredCulture = "en-US",
            paymentTermsDays = 30,
            addressStreet = "Via Roma 1",
            addressPostCode = "20121",
            addressCity = "Milano",
            addressCountry = "IT",
        }));

        using var writers = new WritersObserved();
        var read = await ReadJsonAsync(await accountant.GetAsync($"api/customers/{created.GetProperty("id").GetGuid()}"));

        writers.Seen.Should().Contain("serializer").And.NotContain("generated");
        read.GetProperty("name").GetString().Should().Be("Bianchi S.p.A.");
    }

    protected override void ConfigureServices(IServiceCollection services)
        => services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<DayOfWeek>()));
}
