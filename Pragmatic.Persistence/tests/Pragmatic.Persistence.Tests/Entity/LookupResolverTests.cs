using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Xunit;

namespace Pragmatic.Persistence.Tests.Entity;

[Collection(LookupResolverCollection.Name)]
public class LookupResolverTests
{
    public LookupResolverTests()
    {
        LookupResolver.Reset();
    }

    [Fact]
    public void Get_RegisteredCache_ReturnsEntity()
    {
        var cache = new TestLookupCache();
        cache.Add(1, new TestLookup { Id = 1, Name = "Italy" });
        LookupResolver.Register<TestLookup, int>(cache);

        var result = LookupResolver.Get<TestLookup, int>(1);

        result.Name.Should().Be("Italy");
    }

    [Fact]
    public void Get_UnregisteredCache_ThrowsInvalidOperation()
    {
        var act = () => LookupResolver.Get<TestLookup, int>(1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public void TryGet_RegisteredCache_ReturnsTrue()
    {
        var cache = new TestLookupCache();
        cache.Add(1, new TestLookup { Id = 1, Name = "Italy" });
        LookupResolver.Register<TestLookup, int>(cache);

        var found = LookupResolver.TryGet<TestLookup, int>(1, out var value);

        found.Should().BeTrue();
        value!.Name.Should().Be("Italy");
    }

    [Fact]
    public void TryGet_UnregisteredCache_ReturnsFalse()
    {
        var found = LookupResolver.TryGet<TestLookup, int>(1, out var value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Reset_ClearsAllRegistrations()
    {
        var cache = new TestLookupCache();
        cache.Add(1, new TestLookup { Id = 1, Name = "Italy" });
        LookupResolver.Register<TestLookup, int>(cache);

        LookupResolver.Reset();

        var act = () => LookupResolver.Get<TestLookup, int>(1);
        act.Should().Throw<InvalidOperationException>();
    }

    private class TestLookup
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
    }

    private class TestLookupCache : ILookupCache<TestLookup, int>
    {
        private readonly Dictionary<int, TestLookup> _items = new();

        public void Add(int id, TestLookup item) => _items[id] = item;

        public TestLookup Get(int id) =>
            _items.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException();

        public bool TryGet(int id, out TestLookup? value) => _items.TryGetValue(id, out value);
        public IReadOnlyList<TestLookup> GetAll() => _items.Values.ToList();
    }
}
