using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Data.Providers;

namespace Pragmatic.Documents.Templating.Tests;

public class ProviderTests
{
    private static readonly string FixturesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures");

    // --- JsonFileDataSource ---

    [Fact]
    public async Task JsonFileDataSource_ReadsAndDeserializes()
    {
        var filePath = CreateJsonFixture("company.json",
            new { Name = "Pragmatic", Address = "Via Roma 42", VatId = "IT123" });

        var provider = new JsonFileDataSource<CompanyDto>("company", filePath);

        provider.Name.Should().Be("company");
        provider.ValueType.Should().Be(typeof(CompanyDto));

        var result = await provider.ResolveAsync();
        result.Should().BeOfType<CompanyDto>();
        ((CompanyDto)result!).Name.Should().Be("Pragmatic");
    }

    [Fact]
    public async Task JsonFileListDataSource_ReadsListOfObjects()
    {
        var items = new[]
        {
            new { Description = "Widget", Qty = 1, Price = 99.0, Total = 99.0 },
            new { Description = "Gadget", Qty = 2, Price = 49.0, Total = 98.0 }
        };
        var filePath = CreateJsonFixture("items.json", items);

        var provider = new JsonFileListDataSource<InvoiceItemDto>("items", filePath);

        provider.ValueType.Should().Be(typeof(List<InvoiceItemDto>));

        var result = await provider.ResolveAsync();
        var list = result.Should().BeOfType<List<InvoiceItemDto>>().Subject;
        list.Should().HaveCount(2);
        list[0].Description.Should().Be("Widget");
    }

    [Fact]
    public async Task JsonFile_InCatalog_ResolvesViaDataContext()
    {
        var filePath = CreateJsonFixture("company2.json",
            new { Name = "Test Corp", Address = "Street 1", VatId = "XX999" });

        var catalog = new DataSourceCatalog();
        catalog.AddProvider(new JsonFileDataSource<CompanyDto>("company", filePath));

        var ctx = catalog.ToDataContext();
        var name = await ctx.ResolveAsync("company.Name");
        name.Should().Be("Test Corp");
    }

    // --- SqlDataSource (test with in-memory structure) ---

    [Fact]
    public void SqlDataSource_HasCorrectValueType()
    {
        var provider = new SqlDataSource("data",
            () => throw new NotSupportedException("not called in this test"),
            "SELECT 1");

        provider.Name.Should().Be("data");
        provider.ValueType.Should().Be(typeof(List<Dictionary<string, object?>>));
    }

    [Fact]
    public void SqlDataSource_SingleRow_HasCorrectValueType()
    {
        var provider = new SqlDataSource("data",
            () => throw new NotSupportedException("not called in this test"),
            "SELECT 1", singleRow: true);

        provider.ValueType.Should().Be(typeof(Dictionary<string, object?>));
    }

    // --- Extension methods ---

    [Fact]
    public void AddSql_RegistersProvider()
    {
        var catalog = new DataSourceCatalog()
            .AddSql("orders",
                () => throw new NotSupportedException(),
                "SELECT * FROM Orders");

        catalog.HasSource("orders").Should().BeTrue();
    }

    [Fact]
    public async Task AddJsonFile_Extension_Works()
    {
        var filePath = CreateJsonFixture("ext-test.json",
            new { Name = "ExtTest", Address = "A", VatId = "B" });

        var catalog = new DataSourceCatalog();
        catalog.AddProvider(new JsonFileDataSource<CompanyDto>("ext", filePath));

        var ctx = catalog.ToDataContext();
        var name = await ctx.ResolveAsync("ext.Name");
        name.Should().Be("ExtTest");
    }

    // --- Helpers ---

    private static string CreateJsonFixture(string fileName, object data)
    {
        Directory.CreateDirectory(FixturesDir);
        var filePath = Path.Combine(FixturesDir, fileName);
        File.WriteAllText(filePath, JsonSerializer.Serialize(data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return filePath;
    }
}
