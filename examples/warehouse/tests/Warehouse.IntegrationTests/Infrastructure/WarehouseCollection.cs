namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>One running example for the whole suite; every test class shares it.</summary>
[CollectionDefinition(Name)]
public sealed class WarehouseCollection : ICollectionFixture<WarehouseFixture>
{
    public const string Name = "Warehouse";
}
