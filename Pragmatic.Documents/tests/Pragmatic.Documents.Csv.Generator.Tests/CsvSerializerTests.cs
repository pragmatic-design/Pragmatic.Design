using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv.Generator.Tests;

public class CsvSerializerTests
{
    [Fact]
    public void Generated_Headers_AreCorrect()
    {
        OrderDto.Csv.Headers.Should().Equal("N. Ordine", "Data", "Totale", "Active");
    }

    [Fact]
    public void Simple_Headers_ArePropertyNames()
    {
        SimpleDto.Csv.Headers.Should().Equal("Name", "Age", "Score");
    }

    [Fact]
    public void Write_SimpleDto()
    {
        var items = new[]
        {
            new SimpleDto { Name = "Alice", Age = 30, Score = 95.5 },
            new SimpleDto { Name = "Bob", Age = 25, Score = 88.0 }
        };

        var csv = GetString(s => SimpleDto.Csv.Write(s, items));

        csv.Should().Contain("Name,Age,Score");
        csv.Should().Contain("Alice,30,95.5");
        csv.Should().Contain("Bob,25,88");
    }

    [Fact]
    public void Write_OrderDto_WithFormat()
    {
        var items = new[]
        {
            new OrderDto
            {
                Number = "ORD-001",
                Date = new DateTime(2026, 4, 10),
                Total = 1234.56m,
                Active = true
            }
        };

        var options = CsvOptions.Italian;
        var csv = GetString(s => OrderDto.Csv.Write(s, items, options));

        csv.Should().Contain("N. Ordine;Data;Totale;Active");
        csv.Should().Contain("ORD-001;10/04/2026;1.234,56;true");
    }

    [Fact]
    public void WriteToArray_Works()
    {
        var items = new[] { new SimpleDto { Name = "Test", Age = 1, Score = 0 } };
        var bytes = SimpleDto.Csv.WriteToArray(items);

        bytes.Should().NotBeEmpty();
        Encoding.UTF8.GetString(bytes).Should().Contain("Test");
    }

    [Fact]
    public void Read_SimpleDto()
    {
        var csv = "Name,Age,Score\r\nAlice,30,95.5\r\nBob,25,88";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var items = SimpleDto.Csv.Read(ms);

        items.Should().HaveCount(2);
        items[0].Name.Should().Be("Alice");
        items[0].Age.Should().Be(30);
        items[0].Score.Should().Be(95.5);
        items[1].Name.Should().Be("Bob");
    }

    [Fact]
    public void Read_OrderDto_WithFormat()
    {
        var csv = "N. Ordine;Data;Totale;Active\r\nORD-001;10/04/2026;1234,56;true";
        var options = CsvOptions.Italian;
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var items = OrderDto.Csv.Read(ms, options);

        items.Should().HaveCount(1);
        items[0].Number.Should().Be("ORD-001");
        items[0].Date.Should().Be(new DateTime(2026, 4, 10));
        items[0].Total.Should().Be(1234.56m);
        items[0].Active.Should().BeTrue();
    }

    [Fact]
    public void Read_FromBytes()
    {
        var csv = Encoding.UTF8.GetBytes("Name,Age,Score\r\nTest,42,100");
        var items = SimpleDto.Csv.Read(csv);

        items.Should().HaveCount(1);
        items[0].Name.Should().Be("Test");
        items[0].Age.Should().Be(42);
    }

    [Fact]
    public void Roundtrip_SimpleDto()
    {
        var original = new[]
        {
            new SimpleDto { Name = "Alice", Age = 30, Score = 95.5 },
            new SimpleDto { Name = "Bob", Age = 25, Score = 88.0 }
        };

        var bytes = SimpleDto.Csv.WriteToArray(original);
        var read = SimpleDto.Csv.Read(bytes);

        read.Should().HaveCount(2);
        read[0].Name.Should().Be("Alice");
        read[0].Age.Should().Be(30);
        read[0].Score.Should().Be(95.5);
    }

    [Fact]
    public void Roundtrip_TypedDto_EnumGuidTimeSpan()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var original = new[]
        {
            new TypedDto
            {
                Name = "A", Status = OrderStatus.Shipped, OptionalStatus = OrderStatus.Delivered,
                Id = id, Duration = TimeSpan.FromMinutes(90),
            },
            new TypedDto { Name = "B", Status = OrderStatus.Pending, OptionalStatus = null, Id = Guid.Empty, Duration = TimeSpan.Zero },
        };

        var read = TypedDto.Csv.Read(TypedDto.Csv.WriteToArray(original));

        read.Should().HaveCount(2);
        read[0].Status.Should().Be(OrderStatus.Shipped);
        read[0].OptionalStatus.Should().Be(OrderStatus.Delivered);
        read[0].Id.Should().Be(id);
        read[0].Duration.Should().Be(TimeSpan.FromMinutes(90));
        read[1].OptionalStatus.Should().BeNull();
        read[1].Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Roundtrip_OrderDto_Italian()
    {
        var options = CsvOptions.Italian;
        var original = new[]
        {
            new OrderDto { Number = "ORD-001", Date = new DateTime(2026, 1, 15), Total = 4500.00m, Active = true },
            new OrderDto { Number = "ORD-002", Date = new DateTime(2026, 2, 28), Total = 12300.50m, Active = false }
        };

        using var ms = new MemoryStream();
        OrderDto.Csv.Write(ms, original, options);
        ms.Position = 0;
        var read = OrderDto.Csv.Read(ms, options);

        read.Should().HaveCount(2);
        read[0].Number.Should().Be("ORD-001");
        read[0].Date.Should().Be(new DateTime(2026, 1, 15));
        read[0].Total.Should().Be(4500.00m);
        read[0].Active.Should().BeTrue();
        read[1].Number.Should().Be("ORD-002");
        read[1].Active.Should().BeFalse();
    }

    [Fact]
    public void Ignore_SkipsProperty()
    {
        IgnoreDto.Csv.Headers.Should().Equal("Name", "Value");
        IgnoreDto.Csv.Headers.Should().NotContain("Secret");
    }

    [Fact]
    public void NullableDto_Headers()
    {
        NullableDto.Csv.Headers.Should().Equal("Name", "OptionalAge", "OptionalTotal");
    }

    [Fact]
    public void NullableDto_Write_WithNulls()
    {
        var items = new[]
        {
            new NullableDto { Name = "Alice", OptionalAge = 30, OptionalTotal = 99.5m },
            new NullableDto { Name = "Bob", OptionalAge = null, OptionalTotal = null }
        };

        var csv = GetString(s => NullableDto.Csv.Write(s, items));

        csv.Should().Contain("Alice,30,99.5");
        csv.Should().Contain("Bob,,");
    }

    [Fact]
    public void NullableDto_Roundtrip()
    {
        var original = new[]
        {
            new NullableDto { Name = "Alice", OptionalAge = 30, OptionalTotal = 99.5m },
            new NullableDto { Name = "Bob", OptionalAge = null, OptionalTotal = null }
        };

        var bytes = NullableDto.Csv.WriteToArray(original);
        var read = NullableDto.Csv.Read(bytes);

        read.Should().HaveCount(2);
        read[0].OptionalAge.Should().Be(30);
        read[0].OptionalTotal.Should().Be(99.5m);
        read[1].OptionalAge.Should().BeNull();
        read[1].OptionalTotal.Should().BeNull();
    }

    [Fact]
    public void AComputedProperty_IsAColumn_AndItsValueIsWritten()
    {
        InvoiceLineDto.Csv.Headers.Should().Equal("Net", "Tax", "Total");

        var csv = GetString(s => InvoiceLineDto.Csv.Write(s, [new InvoiceLineDto { Net = 100m, Tax = 22m }]));

        csv.Should().Contain("122", "the export carries the computed value, which is why the column is there");
    }

    /// <summary>
    ///     The control: reading the same file back takes the columns that can be set, and the computed
    ///     one recomputes from them.
    /// </summary>
    [Fact]
    public void AComputedProperty_RecomputesOnRead()
    {
        var read = InvoiceLineDto.Csv.Read(InvoiceLineDto.Csv.WriteToArray([new InvoiceLineDto { Net = 100m, Tax = 22m }]));

        read.Should().HaveCount(1);
        read[0].Net.Should().Be(100m);
        read[0].Tax.Should().Be(22m);
        read[0].Total.Should().Be(122m);
    }

    private static string GetString(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        write(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
