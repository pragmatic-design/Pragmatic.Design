using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Result.Http;

namespace Pragmatic.Configuration.Management.Tests.Unit;

public class SetConfigValueTests
{
    private static SetConfigValue Build(IConfigurationStore store, string key, string value, string? tenantId = null)
        => ActionFieldInjector.Inject(ActionFieldInjector.Inject(ActionFieldInjector.Inject(
            new SetConfigValue { Key = key, Value = value, TenantId = tenantId }, "_store", store), "_currentUser", TestCaller.Operator),
            "_catalog", TestCatalog.App());

    [Fact]
    public async Task Execute_ValidKeyValue_PersistsAndSucceeds()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Name", "MyApp");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        store.SetCalls.Should().ContainSingle();
        store.SetCalls[0].Should().Be(("App:Name", "MyApp", (string?)null));
        (await store.GetAsync("App:Name")).Should().Be("MyApp");
    }

    [Fact]
    public async Task Execute_WithTenantId_PassesTenantToStore()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Name", "TenantApp", "tenant-1");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        store.SetCalls.Should().ContainSingle();
        store.SetCalls[0].TenantId.Should().Be("tenant-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_BlankKey_ReturnsBadRequestAndDoesNotWrite(string key)
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, key, "value");

        var result = await action.Execute();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BadRequestError>();
        store.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NullValue_ReturnsBadRequestAndDoesNotWrite()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Name", value: null!);

        var result = await action.Execute();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BadRequestError>();
        store.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_EmptyStringValue_IsAllowed()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Name", "");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        store.SetCalls.Should().ContainSingle();
        store.SetCalls[0].Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NullStore_Throws()
    {
        var action = new SetConfigValue { Key = "App:Name", Value = "v" };

        var act = () => action.Execute();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
