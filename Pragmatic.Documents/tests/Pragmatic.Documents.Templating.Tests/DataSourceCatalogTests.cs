using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Templating.Tests;

public record CompanyDto(string Name, string Address, string VatId);
public record InvoiceDto(string Number, DateTimeOffset Date, decimal Total);
public record InvoiceItemDto(string Description, int Qty, decimal Price, decimal Total);

public class DataSourceCatalogTests
{
    [Fact]
    public void Add_StaticSource_RegistersCorrectly()
    {
        var catalog = new DataSourceCatalog()
            .Add("company", new CompanyDto("Pragmatic", "Via Roma 1", "IT123"));

        catalog.Count.Should().Be(1);
        catalog.SourceNames.Should().Contain("company");
        catalog.HasSource("company").Should().BeTrue();
    }

    [Fact]
    public void Add_DuplicateName_ThrowsArgumentException()
    {
        var catalog = new DataSourceCatalog()
            .Add("company", new CompanyDto("A", "B", "C"));

        var act = () => catalog.Add("company", new CompanyDto("X", "Y", "Z"));

        act.Should().Throw<ArgumentException>().WithMessage("*company*already registered*");
    }

    [Fact]
    public void Add_CaseInsensitive_DetectsDuplicates()
    {
        var catalog = new DataSourceCatalog()
            .Add("Company", new CompanyDto("A", "B", "C"));

        var act = () => catalog.Add("company", new CompanyDto("X", "Y", "Z"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddAsync_ResolvesOnDemand()
    {
        var resolved = false;
        var catalog = new DataSourceCatalog()
            .AddAsync<CompanyDto>("company", async ct =>
            {
                resolved = true;
                await Task.CompletedTask;
                return new CompanyDto("Pragmatic", "Via Roma 1", "IT123");
            });

        resolved.Should().BeFalse();

        var provider = catalog.GetProvider("company");
        provider.Should().NotBeNull();
        provider!.ValueType.Should().Be(typeof(CompanyDto));

        var value = await provider.ResolveAsync();
        resolved.Should().BeTrue();
        value.Should().BeOfType<CompanyDto>();
    }

    [Fact]
    public void Merge_CombinesTwoCatalogs()
    {
        var catalog1 = new DataSourceCatalog().Add("company", new CompanyDto("A", "B", "C"));
        var catalog2 = new DataSourceCatalog().Add("invoice", new InvoiceDto("001", DateTimeOffset.Now, 100));

        catalog1.Merge(catalog2);

        catalog1.Count.Should().Be(2);
        catalog1.HasSource("company").Should().BeTrue();
        catalog1.HasSource("invoice").Should().BeTrue();
    }

    [Fact]
    public void Merge_DuplicateWithoutOverride_Throws()
    {
        var catalog1 = new DataSourceCatalog().Add("data", new CompanyDto("A", "B", "C"));
        var catalog2 = new DataSourceCatalog().Add("data", new CompanyDto("X", "Y", "Z"));

        var act = () => catalog1.Merge(catalog2);

        act.Should().Throw<ArgumentException>().WithMessage("*data*exists in both*");
    }

    [Fact]
    public void Merge_DuplicateWithOverride_Replaces()
    {
        var catalog1 = new DataSourceCatalog().Add("data", new CompanyDto("A", "B", "C"));
        var catalog2 = new DataSourceCatalog().Add("data", new CompanyDto("X", "Y", "Z"));

        catalog1.Merge(catalog2, allowOverride: true);

        catalog1.Count.Should().Be(1);
    }

    [Fact]
    public async Task ToDataContext_StaticSource_ResolvesProperties()
    {
        var catalog = new DataSourceCatalog()
            .Add("company", new CompanyDto("Pragmatic S.r.l.", "Via Roma 42", "IT123456789"));

        var ctx = catalog.ToDataContext();

        var name = await ctx.ResolveAsync("company.Name");
        name.Should().Be("Pragmatic S.r.l.");

        var address = await ctx.ResolveAsync("company.Address");
        address.Should().Be("Via Roma 42");
    }

    [Fact]
    public async Task ToDataContext_AsyncSource_ResolvesOnDemand()
    {
        var callCount = 0;
        var catalog = new DataSourceCatalog()
            .AddAsync<InvoiceDto>("invoice", async ct =>
            {
                callCount++;
                await Task.CompletedTask;
                return new InvoiceDto("2026-042", DateTimeOffset.Now, 7320m);
            });

        var ctx = catalog.ToDataContext();

        var number = await ctx.ResolveAsync("invoice.Number");
        number.Should().Be("2026-042");
        callCount.Should().Be(1);

        // Second access should use cache
        var total = await ctx.ResolveAsync("invoice.Total");
        total.Should().Be(7320m);
        callCount.Should().Be(1); // still 1, cached
    }

    [Fact]
    public async Task ToDataContext_CollectionSource_IteratesTypedItems()
    {
        var items = new List<InvoiceItemDto>
        {
            new("Licenza", 1, 4000m, 4000m),
            new("Supporto", 1, 1200m, 1200m)
        };

        var catalog = new DataSourceCatalog()
            .Add("invoice", new { Items = items });

        var ctx = catalog.ToDataContext();

        var collection = await ctx.ResolveCollectionAsync("invoice.Items");
        collection.Should().NotBeNull();
        collection!.Cast<InvoiceItemDto>().Should().HaveCount(2);

        // Navigate into first item
        var childCtx = ctx.CreateChildScope("item", items[0]);
        var desc = await childCtx.ResolveAsync("item.Description");
        desc.Should().Be("Licenza");
    }

    [Fact]
    public async Task Warnings_UnresolvedSource_EmitsWarning()
    {
        var catalog = new DataSourceCatalog()
            .Add("company", new CompanyDto("A", "B", "C"));

        var ctx = catalog.ToDataContext();

        var result = await ctx.ResolveAsync("nonexistent.name");
        result.Should().BeNull();

        ctx.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("nonexistent.name");
    }

    [Fact]
    public async Task Warnings_UnresolvedProperty_EmitsWarning()
    {
        var catalog = new DataSourceCatalog()
            .Add("company", new CompanyDto("A", "B", "C"));

        var ctx = catalog.ToDataContext();

        var result = await ctx.ResolveAsync("company.NonExistent");
        result.Should().BeNull();

        ctx.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("company.NonExistent");
    }

    [Fact]
    public void ProviderValueType_IsPreserved()
    {
        var catalog = new DataSourceCatalog()
            .Add("company", new CompanyDto("A", "B", "C"))
            .AddAsync<InvoiceDto>("invoice", _ => ValueTask.FromResult(new InvoiceDto("001", DateTimeOffset.Now, 100)));

        catalog.GetProvider("company")!.ValueType.Should().Be(typeof(CompanyDto));
        catalog.GetProvider("invoice")!.ValueType.Should().Be(typeof(InvoiceDto));
    }
}
