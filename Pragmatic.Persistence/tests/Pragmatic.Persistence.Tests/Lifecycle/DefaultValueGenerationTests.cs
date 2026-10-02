using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.Tests.Lifecycle;

public sealed class DefaultValueGenerationTests
{
    // --- DefaultValueAttribute ---

    [Fact]
    public void DefaultValueAttribute_StoresStringValue()
    {
        var attr = new DefaultValueAttribute("EUR");
        attr.Value.Should().Be("EUR");
    }

    [Fact]
    public void DefaultValueAttribute_StoresIntValue()
    {
        var attr = new DefaultValueAttribute(1);
        attr.Value.Should().Be(1);
    }

    [Fact]
    public void DefaultValueAttribute_StoresBoolValue()
    {
        var attr = new DefaultValueAttribute(true);
        attr.Value.Should().Be(true);
    }

    [Fact]
    public void DefaultValueAttribute_TargetsProperty()
    {
        var usage = typeof(DefaultValueAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Property);
    }

    // --- ComputedDefaultAttribute ---

    [Fact]
    public void ComputedDefaultAttribute_TargetsProperty()
    {
        var usage = typeof(ComputedDefaultAttribute<TestEntity, string, TestStringGenerator>)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Property);
    }

    [Fact]
    public void ComputedDefaultAttribute_CanBeInstantiated()
    {
        var attr = new ComputedDefaultAttribute<TestEntity, string, TestStringGenerator>();
        attr.Should().NotBeNull();
    }

    // --- IDefaultValueGenerator ---

    [Fact]
    public async Task DefaultValueGenerator_GeneratesValue()
    {
        var generator = new TestStringGenerator();
        var entity = new TestEntity();
        var context = new LifecycleContext { Now = DateTimeOffset.UtcNow };

        var result = await generator.GenerateAsync(entity, context, CancellationToken.None);

        result.Should().Be("GEN-001");
    }

    [Fact]
    public async Task DefaultValueGenerator_CanUseEntityState()
    {
        var generator = new TestEntityAwareGenerator();
        var entity = new TestEntity { Name = "Test" };
        var context = LifecycleContext.At(DateTimeOffset.UnixEpoch);

        var result = await generator.GenerateAsync(entity, context, CancellationToken.None);

        result.Should().Be("DEFAULT-Test");
    }

    [Fact]
    public async Task DefaultValueGenerator_CanUseContext()
    {
        var generator = new TestContextAwareGenerator();
        var entity = new TestEntity();
        var fixedNow = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var context = new LifecycleContext { Now = fixedNow };

        var result = await generator.GenerateAsync(entity, context, CancellationToken.None);

        result.Should().Be("2025-001");
    }

    // --- IEntityLifecycle ---

    [Fact]
    public void EntityLifecycle_OnCreating_DefaultIsNoOp()
    {
        IEntityLifecycle<TestEntity> lifecycle = new NoOpLifecycle();
        var entity = new TestEntity();

        // Should not throw — DIM provides no-op
        lifecycle.OnCreating(entity, LifecycleContext.At(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void EntityLifecycle_OnSaving_DefaultIsNoOp()
    {
        IEntityLifecycle<TestEntity> lifecycle = new NoOpLifecycle();
        var entity = new TestEntity();

        lifecycle.OnSaving(entity, LifecycleContext.At(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void EntityLifecycle_OnCreating_CanSetDefaults()
    {
        IEntityLifecycle<TestEntity> lifecycle = new TestEntityLifecycle();
        var entity = new TestEntity();
        var context = LifecycleContext.At(DateTimeOffset.UnixEpoch) with { UserId = "admin" };

        lifecycle.OnCreating(entity, context);

        entity.CreatedBy.Should().Be("admin");
    }

    [Fact]
    public void EntityLifecycle_OnSaving_CanModifyEntity()
    {
        IEntityLifecycle<TestEntity> lifecycle = new TestEntityLifecycle();
        var entity = new TestEntity { Name = "  trimmed  " };

        lifecycle.OnSaving(entity, LifecycleContext.At(DateTimeOffset.UnixEpoch));

        entity.Name.Should().Be("trimmed");
    }

    #region Test Helpers

    private sealed class TestEntity
    {
        public string Name { get; set; } = "";
        public string CreatedBy { get; set; } = "";
    }

    private sealed class TestStringGenerator : IDefaultValueGenerator<TestEntity, string>
    {
        public Task<string> GenerateAsync(TestEntity entity, LifecycleContext context, CancellationToken ct)
            => Task.FromResult("GEN-001");
    }

    private sealed class TestEntityAwareGenerator : IDefaultValueGenerator<TestEntity, string>
    {
        public Task<string> GenerateAsync(TestEntity entity, LifecycleContext context, CancellationToken ct)
            => Task.FromResult($"DEFAULT-{entity.Name}");
    }

    private sealed class TestContextAwareGenerator : IDefaultValueGenerator<TestEntity, string>
    {
        public Task<string> GenerateAsync(TestEntity entity, LifecycleContext context, CancellationToken ct)
            => Task.FromResult($"{context.Now.Year}-001");
    }

    private sealed class NoOpLifecycle : IEntityLifecycle<TestEntity>;

    private sealed class TestEntityLifecycle : IEntityLifecycle<TestEntity>
    {
        public void OnCreating(TestEntity entity, LifecycleContext context)
        {
            if (context.UserId is not null)
                entity.CreatedBy = context.UserId;
        }

        public void OnSaving(TestEntity entity, LifecycleContext context)
        {
            entity.Name = entity.Name.Trim();
        }
    }

    #endregion
}
