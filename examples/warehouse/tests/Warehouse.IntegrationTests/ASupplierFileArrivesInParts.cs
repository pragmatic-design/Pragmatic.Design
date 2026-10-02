using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Batch;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Stock.Imports.Messages;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A supplier's stock file imported as a batch in parts, applied by both Stock instances,
///     with progress someone watches: parts done, failed and in total, and every row refused with why.
/// </summary>
/// <remarks>
///     The file goes through the gateway, as a clerk would send it. The parts go over the broker, so
///     whichever Stock instance is free applies one; the progress is a table both of them write.
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class ASupplierFileArrivesInParts(WarehouseFixture warehouse)
{
    private const string Parts = "stock.import-file-part";

    [Fact]
    public async Task AThousandRowsInPartsOfAHundred_AllTenPartsDone_AndEachSkuHasItsRows()
    {
        using var clerk = StockCalls.ThroughTheGateway(warehouse, StockCalls.Clerk);
        var (products, location) = await CatalogueAsync(10);
        var rows = Enumerable.Range(0, 1000)
            .Select(i => (Sku: products[i % 10].Sku, Location: location, Quantity: (i % 7) + 1))
            .ToList();

        var started = await ImportAsync(clerk, Csv(rows.Select(r => $"{r.Sku},{r.Location},{r.Quantity}")), partSize: 100);
        started.GetProperty("parts").GetInt32().Should().Be(10);

        var progress = await UntilFinishedAsync(clerk, started.GetProperty("importId").GetGuid());
        (progress.GetProperty("done").GetInt32(), progress.GetProperty("failed").GetInt32(), progress.GetProperty("parts").GetInt32())
            .Should().Be((10, 0, 10));

        foreach (var (id, sku) in products)
            (await OnHandAsync(clerk, id)).Should().Be(rows.Where(r => r.Sku == sku).Sum(r => r.Quantity), sku);
    }

    [Fact]
    public async Task ThreeInvalidRows_AreReportedWithTheirReason_TheOthersApplied_AndTheirPartsFailed()
    {
        using var clerk = StockCalls.ThroughTheGateway(warehouse, StockCalls.Clerk);
        var (products, location) = await CatalogueAsync(1);
        var sku = products[0].Sku;
        var lines = Enumerable.Range(0, 30).Select(_ => $"{sku},{location},2").ToList();
        lines[3] = $"SKU-NOBODY-HAS,{location},2";   // line 5
        lines[12] = $"{sku},{location},abc";          // line 14
        lines[25] = $"{sku},NO-SUCH-BAY,2";           // line 27

        var started = await ImportAsync(clerk, Csv(lines), partSize: 10);
        var progress = await UntilFinishedAsync(clerk, started.GetProperty("importId").GetGuid());

        var rejected = progress.GetProperty("rejectedRows").EnumerateArray()
            .Select(r => (r.GetProperty("line").GetInt32(), r.GetProperty("reason").GetString()))
            .ToList();
        rejected.Select(r => r.Item1).Should().BeEquivalentTo([5, 14, 27]);
        rejected.Should().OnlyContain(r => !string.IsNullOrEmpty(r.Item2));
        progress.GetProperty("failedParts").EnumerateArray().Select(p => p.GetInt32()).Should().BeEquivalentTo([0, 1, 2]);
        (progress.GetProperty("done").GetInt32(), progress.GetProperty("failed").GetInt32()).Should().Be((0, 3));

        (await OnHandAsync(clerk, products[0].Id)).Should().Be(27 * 2, "the 27 valid rows were applied");
    }

    /// <summary>The control: a part delivered a second time applies nothing a second time.</summary>
    [Fact]
    public async Task APartDeliveredTwice_AppliesItsRowsOnce()
    {
        using var clerk = StockCalls.ThroughTheGateway(warehouse, StockCalls.Clerk);
        var (products, location) = await CatalogueAsync(1);
        var lines = Enumerable.Range(0, 20).Select(_ => $"{products[0].Sku},{location},3").ToList();

        var started = await ImportAsync(clerk, Csv(lines), partSize: 10);
        var importId = started.GetProperty("importId").GetGuid();
        await UntilFinishedAsync(clerk, importId);
        (await OnHandAsync(clerk, products[0].Id)).Should().Be(60);

        // Part 0 again, as the broker would redeliver it: the same rows, the same batch headers.
        var consumed = await warehouse.Broker.AcknowledgedOnAsync(Parts);
        await RedeliverAsync(importId, new ImportFilePart(0,
            [.. Enumerable.Range(0, 10).Select(i => new ImportFileRow(i + 2, products[0].Sku, location, "3"))]));
        await WarehouseWaits.UntilAsync(() => warehouse.Broker.AcknowledgedOnAsync(Parts), acked => acked > consumed,
            "the redelivered part consumed");

        (await OnHandAsync(clerk, products[0].Id)).Should().Be(60, "the part was applied once");
    }

    [Fact]
    public async Task AFileWithTheWrongHeader_Is400_AndNothingIsDispatched()
    {
        using var clerk = StockCalls.ThroughTheGateway(warehouse, StockCalls.Clerk);

        using var refused = await PostFileAsync(clerk, "product,bay,qty\nA,B,1\n", partSize: 10);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
    }

    private async Task<(List<(Guid Id, string Sku)> Products, string Location)> CatalogueAsync(int products)
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var made = new List<(Guid, string)>();
        for (var i = 0; i < products; i++)
            made.Add(await StockCalls.NewProductAsync(manager));

        var code = $"IMP-{Guid.NewGuid():N}"[..18];
        using var location = await manager.PostAsJsonAsync("api/locations", new { code, description = "Where an import lands" });
        location.EnsureSuccessStatusCode();
        return (made, code);
    }

    private async Task RedeliverAsync(Guid importId, ImportFilePart part)
    {
        await using var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var context = MessageContext.New() with
        {
            Headers = new Dictionary<string, string>
            {
                [BatchHeaders.BatchId] = importId.ToString(),
                [BatchHeaders.ItemIndex] = part.PartIndex.ToString(CultureInfo.InvariantCulture),
                [BatchHeaders.TotalItems] = "2",
            },
        };
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(part, context);
    }

    private static string Csv(IEnumerable<string> lines) => "sku,location,quantity\n" + string.Join('\n', lines) + "\n";

    private static async Task<JsonElement> ImportAsync(HttpClient clerk, string csv, int partSize)
    {
        using var response = await PostFileAsync(clerk, csv, partSize);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"POST api/imports answered {(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PostFileAsync(HttpClient clerk, string csv, int partSize)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "supplier.csv");
        form.Add(new StringContent(partSize.ToString(CultureInfo.InvariantCulture)), "partSize");
        return clerk.PostAsync("api/imports", form);
    }

    private static Task<JsonElement> UntilFinishedAsync(HttpClient clerk, Guid importId)
        => WarehouseWaits.UntilAsync(
            async () =>
            {
                using var response = await clerk.GetAsync($"api/imports/{importId}");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"GET api/imports/{importId} answered {(int)response.StatusCode}: {body}");
                return JsonDocument.Parse(body).RootElement.Clone();
            },
            progress => progress.GetProperty("finished").GetBoolean(),
            $"import {importId} finished");

    private static async Task<int> OnHandAsync(HttpClient client, Guid productId)
        => (await StockCalls.LevelsOfAsync(client, productId)).Sum(level => level.GetProperty("onHand").GetInt32());
}
