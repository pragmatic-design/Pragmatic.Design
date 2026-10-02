using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Management.Actions;

namespace Pragmatic.Configuration.Management.Tests.Unit;

public class GetConfigValuesTests
{
    private static GetConfigValues Build(IConfigurationStore store, string prefix = "", string? tenantId = null)
        => ActionFieldInjector.Inject(ActionFieldInjector.Inject(new GetConfigValues { Prefix = prefix, TenantId = tenantId }, "_store", store), "_currentUser", TestCaller.Operator);

    [Fact]
    public async Task Execute_BasePrefix_ReturnsMatchingValues()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "MyApp");
        await store.SetAsync("App:Timeout", "30");
        var action = Build(store, "App");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainKey("App:Name").WhoseValue.Should().Be("MyApp");
        result.Value.Should().ContainKey("App:Timeout").WhoseValue.Should().Be("30");
    }

    [Fact]
    public async Task Execute_EmptyStore_ReturnsEmptyDictionary()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store);

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WithTenantId_UsesTenantSectionOverload()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "TenantApp", "tenant-1");
        var action = Build(store, "App", "tenant-1");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainKey("App:Name").WhoseValue.Should().Be("TenantApp");
        store.SectionCalls.Should().ContainSingle();
        store.SectionCalls[0].Should().Be(("App", "tenant-1"));
    }

    [Fact]
    public async Task Execute_WithoutTenantId_UsesBaseSectionOverload()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App");

        await action.Execute();

        store.SectionCalls.Should().ContainSingle();
        store.SectionCalls[0].TenantId.Should().BeNull();
    }

    [Fact]
    public async Task Execute_ReturnsMutableCopy_NotBackingReference()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "MyApp");
        var action = Build(store, "App");

        var result = await action.Execute();
        var dictionary = result.Value;

        Action act = () => dictionary.Add("App:Extra", "x");
        act.Should().NotThrow();
        dictionary.Should().ContainKey("App:Extra");
    }

    [Fact]
    public async Task Execute_NullStore_Throws()
    {
        var action = new GetConfigValues();

        var act = () => action.Execute();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
