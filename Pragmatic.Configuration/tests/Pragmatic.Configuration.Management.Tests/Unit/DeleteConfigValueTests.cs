using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Result.Http;

namespace Pragmatic.Configuration.Management.Tests.Unit;

public class DeleteConfigValueTests
{
    private static DeleteConfigValue Build(IConfigurationStore store, string key, string? tenantId = null)
        => ActionFieldInjector.Inject(ActionFieldInjector.Inject(
            new DeleteConfigValue { Key = key, TenantId = tenantId }, "_store", store), "_currentUser", TestCaller.Operator);

    [Fact]
    public async Task Execute_ExistingKey_DeletesAndSucceeds()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "MyApp");
        var action = Build(store, "App:Name");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        store.DeleteCalls.Should().ContainSingle();
        store.DeleteCalls[0].Should().Be(("App:Name", (string?)null));
        (await store.GetAsync("App:Name")).Should().BeNull();
    }

    [Fact]
    public async Task Execute_MissingKey_StillSucceeds()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Missing");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        store.DeleteCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Execute_WithTenantId_PassesTenantToStore()
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, "App:Name", "tenant-1");

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        store.DeleteCalls.Should().ContainSingle();
        store.DeleteCalls[0].TenantId.Should().Be("tenant-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_BlankKey_ReturnsBadRequestAndDoesNotDelete(string key)
    {
        var store = new RecordingConfigurationStore();
        var action = Build(store, key);

        var result = await action.Execute();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BadRequestError>();
        store.DeleteCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NullStore_Throws()
    {
        var action = new DeleteConfigValue { Key = "App:Name" };

        var act = () => action.Execute();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
