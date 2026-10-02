using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Result.Http;

namespace Pragmatic.Configuration.Management.Tests.Unit;

public class GetConfigValueTests
{
    private static GetConfigValue Build(IConfigurationStore store, string key, string? tenantId = null)
        => ActionFieldInjector.Inject(ActionFieldInjector.Inject(new GetConfigValue { Key = key, TenantId = tenantId }, "_store", store), "_currentUser", TestCaller.Operator);

    [Fact]
    public async Task Execute_ExistingBaseKey_ReturnsValue()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "MyApp");
        var action = Build(store, "App:Name");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Key.Should().Be("App:Name");
        result.Value.Value.Should().Be("MyApp");
        result.Value.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task Execute_MissingKey_ReturnsSuccessWithNullValue()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Missing");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().BeNull();
    }

    [Fact]
    public async Task Execute_WithTenantId_UsesTenantOverload()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "TenantApp", "tenant-1");
        var action = Build(store, "App:Name", "tenant-1");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("TenantApp");
        result.Value.TenantId.Should().Be("tenant-1");
        store.GetCalls.Should().ContainSingle();
        store.GetCalls[0].Should().Be(("App:Name", "tenant-1"));
    }

    [Fact]
    public async Task Execute_WithoutTenantId_UsesBaseOverload()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Name");

        await action.Execute();

        store.GetCalls.Should().ContainSingle();
        store.GetCalls[0].TenantId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_BlankKey_ReturnsBadRequest(string key)
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, key);

        var result = await action.Execute();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BadRequestError>();
        store.GetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NullStore_Throws()
    {
        var action = new GetConfigValue { Key = "App:Name" };

        var act = () => action.Execute();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
