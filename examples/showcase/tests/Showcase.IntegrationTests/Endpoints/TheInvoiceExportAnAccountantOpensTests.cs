using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Documents.Csv;
using Pragmatic.Testing.Assertions;
using Showcase.Billing.Dtos;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>[CsvSerializable]</c> and <c>[CsvColumn]</c>: the columns are declared on the
///     row type, and the file says exactly what they declare.
/// </summary>
/// <remarks>
///     <para>
///         The XLSX export beside this one lists its header row and its cells separately, in the
///         same order, by hand. Here the order, the names and the formats live on
///         <see cref="InvoiceCsvRow" /> and the writer is generated from them.
///     </para>
///     <para>
///         ⚠️ Asserted on the <b>content</b> — the header line, a renamed column, an excluded one and
///         a formatted value — and not on "a file came back". A generator emitting the wrong columns
///         produces a perfectly valid CSV of the wrong thing, which is what a non-empty-file
///         assertion calls a pass.
///     </para>
/// </remarks>
public class TheInvoiceExportAnAccountantOpensTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheHeaderLine_IsWhatTheRowTypeDeclares()
    {
        var (headers, _) = await ExportAsync();

        headers.Should().Equal(
            ["Invoice number", "Issued", "Total", "Currency", "Status"],
            "the names and the order are [CsvColumn]'s, not the property names' and not declaration "
            + "order — InvoiceNumber is exported as \"Invoice number\"");
    }

    /// <summary>
    ///     The control: a property marked <c>Ignore</c> is not in the file, under any spelling.
    /// </summary>
    /// <remarks>
    ///     Without it, "the header line is these five" is satisfied by a generator that also appends
    ///     what nobody asked for, and the internal key would be in a file somebody e-mails out.
    /// </remarks>
    [Fact]
    public async Task TheIgnoredColumn_IsNotInTheFile()
    {
        var (headers, _) = await ExportAsync();

        headers.Should().NotContain("Id");
        headers.Should().NotContain(nameof(InvoiceCsvRow.Id));
    }

    [Fact]
    public async Task TheFormattedColumns_AreWrittenAsDeclared()
    {
        var invoiceNumber = await AnInvoiceNumberAsync();

        var (headers, rows) = await ExportAsync();
        var row = rows.Single(r => r[Array.IndexOf(headers, "Invoice number")] == invoiceNumber);

        row[Array.IndexOf(headers, "Issued")].Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$",
            "Issued declares Format = \"yyyy-MM-dd\": a ledger wants the day, not the instant");
        row[Array.IndexOf(headers, "Total")].Should().MatchRegex(@"^\d+\.\d{2}$",
            "Total declares Format = \"0.00\", and the invariant culture is what a CSV crossing a "
            + "border has to use — a comma here would be a second column");
    }

    /// <summary>
    ///     And the file reads back: what was written is what the reader returns.
    /// </summary>
    /// <remarks>
    ///     The generated <c>Read</c> is the other half of the same declaration, and a round trip is
    ///     what says the two agree. Quoting is the part that silently does not: a value with a
    ///     delimiter in it writes fine and reads wrong if only one side handles it.
    /// </remarks>
    [Fact]
    public async Task WhatWasWritten_ReadsBackThroughTheGeneratedReader()
    {
        var invoiceNumber = await AnInvoiceNumberAsync();

        var bytes = await BytesAsync();
        var read = InvoiceCsvRow.Csv.Read(bytes);

        var mine = read.Should().ContainSingle(r => r.InvoiceNumber == invoiceNumber).Subject;
        mine.Currency.Should().Be("EUR");
        mine.TotalAmount.Should().BeGreaterThan(0m, "the amount survives the round trip as a number");
        mine.Id.Should().Be(Guid.Empty, "the ignored column was never written, so it cannot come back");
    }

    private async Task<byte[]> BytesAsync()
    {
        var response = await Client.GetAsync("/api/invoices/export.csv");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<(string[] Headers, List<string[]> Rows)> ExportAsync()
        => CsvReader.Read(await BytesAsync());

    /// <summary>The number of an invoice this test put there, found through the search route.</summary>
    private async Task<string> AnInvoiceNumberAsync()
    {
        var reservationId = await AConfirmedReservationAsync();

        var response = await Client.GetAsync($"/api/invoices/search?reservationId={reservationId}");
        response.EnsureSuccessStatusCode();

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("items");

        items.GetArrayLength().Should().Be(1, "confirming a reservation raises exactly one invoice");
        return items[0].GetProperty("invoiceNumber").GetString()!;
    }

    private async Task<Guid> AConfirmedReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Csv",
            lastName = "Export",
            email = $"csv.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"CS-{Guid.NewGuid():N}"[..12],
            name = $"CsvProp-{Guid.NewGuid():N}"[..20],
            city = "Genoa",
            country = "IT",
            starRating = 3
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "CS Room",
            code = $"CS{Guid.NewGuid():N}"[..3],
            baseRate = 140m,
            totalRooms = 3
        });

        var created = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(12).ToString("O"),
                numberOfGuests = 2
            }
        }, JsonOptions);

        created.EnsureSuccessStatusCode();

        // A bare Guid on the wire, not an envelope: this route answers with the id itself.
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        (await Client.PostAsJsonAsync($"/api/reservations/{reservationId}/confirm", new { }, JsonOptions))
            .EnsureSuccessStatusCode();

        return reservationId;
    }
}
