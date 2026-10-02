using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.Tests.Lifecycle;

public sealed class PresetProviderTests
{
    // --- HasPresetsAttribute ---

    [Fact]
    public void HasPresetsAttribute_TargetsClass()
    {
        var usage = typeof(HasPresetsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void HasPresetsAttribute_CanBeInstantiated()
    {
        var attr = new HasPresetsAttribute();
        attr.Should().NotBeNull();
    }

    // --- PresetProviderAttribute ---

    [Fact]
    public void PresetProviderAttribute_TargetsClass()
    {
        var usage = typeof(PresetProviderAttribute<TestPresetProvider>)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void PresetProviderAttribute_AllowsMultiple()
    {
        var usage = typeof(PresetProviderAttribute<TestPresetProvider>)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void PresetProviderAttribute_Order_DefaultsToZero()
    {
        var attr = new PresetProviderAttribute<TestPresetProvider>();
        attr.Order.Should().Be(0);
    }

    [Fact]
    public void PresetProviderAttribute_Order_CanBeSet()
    {
        var attr = new PresetProviderAttribute<TestPresetProvider> { Order = 10 };
        attr.Order.Should().Be(10);
    }

    // --- IPresetProvider ---

    [Fact]
    public async Task PresetProvider_ReturnsPresetEntities()
    {
        IPresetProvider<Order> provider = new OrderLineItemPresetProvider();
        var order = new Order { PackageId = "PKG-1" };
        var context = LifecycleContext.At(DateTimeOffset.UnixEpoch);

        var presets = await provider.CreatePresetsAsync(order, context, CancellationToken.None);

        presets.Should().HaveCount(2);
        presets.Should().AllBeOfType<LineItem>();
    }

    [Fact]
    public async Task PresetProvider_EmptyWhenNoPackage()
    {
        IPresetProvider<Order> provider = new OrderLineItemPresetProvider();
        var order = new Order();
        var context = LifecycleContext.At(DateTimeOffset.UnixEpoch);

        var presets = await provider.CreatePresetsAsync(order, context, CancellationToken.None);

        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task PresetProvider_CanUseLifecycleContext()
    {
        IPresetProvider<Order> provider = new ContextAwarePresetProvider();
        var order = new Order { PackageId = "PKG-1" };
        var context = LifecycleContext.At(DateTimeOffset.UnixEpoch) with { UserId = "admin" };

        var presets = await provider.CreatePresetsAsync(order, context, CancellationToken.None);

        presets.Should().ContainSingle();
        var item = presets[0].Should().BeOfType<LineItem>().Subject;
        item.CreatedBy.Should().Be("admin");
    }

    #region Test Helpers

    private sealed class Order
    {
        public string? PackageId { get; init; }
    }

    private sealed class LineItem
    {
        public required string Name { get; init; }
        public string CreatedBy { get; init; } = "";
    }

    private sealed class TestPresetProvider : IPresetProvider<Order>
    {
        public Task<IReadOnlyList<object>> CreatePresetsAsync(
            Order parent, LifecycleContext context, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
    }

    private sealed class OrderLineItemPresetProvider : IPresetProvider<Order>
    {
        public Task<IReadOnlyList<object>> CreatePresetsAsync(
            Order parent, LifecycleContext context, CancellationToken ct)
        {
            if (parent.PackageId is null)
                return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

            var items = new object[]
            {
                new LineItem { Name = "Item 1" },
                new LineItem { Name = "Item 2" }
            };

            return Task.FromResult<IReadOnlyList<object>>(items);
        }
    }

    private sealed class ContextAwarePresetProvider : IPresetProvider<Order>
    {
        public Task<IReadOnlyList<object>> CreatePresetsAsync(
            Order parent, LifecycleContext context, CancellationToken ct)
        {
            var items = new object[]
            {
                new LineItem { Name = "Preset", CreatedBy = context.UserId ?? "" }
            };

            return Task.FromResult<IReadOnlyList<object>>(items);
        }
    }

    #endregion
}
