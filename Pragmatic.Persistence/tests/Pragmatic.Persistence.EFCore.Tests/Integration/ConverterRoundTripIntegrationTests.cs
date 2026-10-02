using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Identifiers;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Integration tests for EF Core value converters (ShortGuid and OpaqueId).
///     Validates that values survive a full write/read round-trip through the database.
/// </summary>
public class ConverterRoundTripIntegrationTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContextFactory.Create();

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ShortGuidConverter_RoundTrip_PreservesGuidValue()
    {
        // Arrange
        var externalId = Guid7.New();
        var doc = new TestDocument
        {
            PersistenceId = Guid7.New(),
            Title = "ShortGuid Test",
            ExternalId = externalId
        };

        // Act
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        // Detach so EF re-reads from DB (not identity cache)
        _db.Entry(doc).State = EntityState.Detached;
        var loaded = await _db.Documents.FirstAsync(d => d.PersistenceId == doc.PersistenceId);

        // Assert
        loaded.ExternalId.Should().Be(externalId);
    }

    [Fact]
    public async Task ShortGuidConverter_EmptyGuid_RoundTrips()
    {
        // Arrange
        var doc = new TestDocument
        {
            PersistenceId = Guid7.New(),
            Title = "Empty Guid Test",
            ExternalId = Guid.Empty
        };

        // Act
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        _db.Entry(doc).State = EntityState.Detached;
        var loaded = await _db.Documents.FirstAsync(d => d.PersistenceId == doc.PersistenceId);

        // Assert
        loaded.ExternalId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ShortGuidConverter_MultipleEntities_AllPreserved()
    {
        // Arrange
        var guids = Enumerable.Range(0, 5).Select(_ => Guid7.New()).ToList();
        var docs = guids.Select((g, i) => new TestDocument
        {
            PersistenceId = Guid7.New(),
            Title = $"Doc_{i}",
            ExternalId = g
        }).ToList();

        // Act
        _db.Documents.AddRange(docs);
        await _db.SaveChangesAsync();

        // Detach all to force re-read from DB
        foreach (var doc in docs)
            _db.Entry(doc).State = EntityState.Detached;

        var loaded = await _db.Documents.OrderBy(d => d.Title).ToListAsync();

        // Assert
        loaded.Should().HaveCount(5);
        for (var i = 0; i < 5; i++)
        {
            loaded[i].ExternalId.Should().Be(guids[i]);
        }
    }

    [Fact]
    public async Task OpaqueIdConverter_RoundTrip_PreservesLongValue()
    {
        // Arrange
        const long sequenceNumber = 42L;
        var invoice = new TestInvoice
        {
            PersistenceId = Guid7.New(),
            InvoiceNumber = "INV-001",
            SequenceNumber = sequenceNumber
        };

        // Act
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        _db.Entry(invoice).State = EntityState.Detached;
        var loaded = await _db.Invoices.FirstAsync(i => i.PersistenceId == invoice.PersistenceId);

        // Assert
        loaded.SequenceNumber.Should().Be(sequenceNumber);
    }

    [Fact]
    public async Task OpaqueIdConverter_ZeroValue_RoundTrips()
    {
        // Arrange
        var invoice = new TestInvoice
        {
            PersistenceId = Guid7.New(),
            InvoiceNumber = "INV-ZERO",
            SequenceNumber = 0L
        };

        // Act
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        _db.Entry(invoice).State = EntityState.Detached;
        var loaded = await _db.Invoices.FirstAsync(i => i.PersistenceId == invoice.PersistenceId);

        // Assert
        loaded.SequenceNumber.Should().Be(0L);
    }

    [Fact]
    public async Task OpaqueIdConverter_LargeValue_RoundTrips()
    {
        // Arrange
        const long largeNumber = 999_999_999L;
        var invoice = new TestInvoice
        {
            PersistenceId = Guid7.New(),
            InvoiceNumber = "INV-LARGE",
            SequenceNumber = largeNumber
        };

        // Act
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        _db.Entry(invoice).State = EntityState.Detached;
        var loaded = await _db.Invoices.FirstAsync(i => i.PersistenceId == invoice.PersistenceId);

        // Assert
        loaded.SequenceNumber.Should().Be(largeNumber);
    }

    [Fact]
    public async Task OpaqueIdConverter_SequentialValues_AllPreserved()
    {
        // Arrange
        var invoices = Enumerable.Range(1, 10).Select(i => new TestInvoice
        {
            PersistenceId = Guid7.New(),
            InvoiceNumber = $"INV-SEQ-{i:D3}",
            SequenceNumber = i
        }).ToList();

        // Act
        _db.Invoices.AddRange(invoices);
        await _db.SaveChangesAsync();

        // Detach all
        foreach (var inv in invoices)
            _db.Entry(inv).State = EntityState.Detached;

        var loaded = await _db.Invoices.OrderBy(i => i.InvoiceNumber).ToListAsync();

        // Assert
        loaded.Should().HaveCount(10);
        for (var i = 0; i < 10; i++)
        {
            loaded[i].SequenceNumber.Should().Be(i + 1);
        }
    }
}
