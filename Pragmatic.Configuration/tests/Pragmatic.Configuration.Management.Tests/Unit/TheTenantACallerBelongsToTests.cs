using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Identity;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>
///     A caller that belongs to a tenant manages that tenant's configuration and nothing else. The
///     management permissions are flat — <c>configuration.values.write</c> says nothing about which
///     tenant — so without this binding an administrator of one tenant could read or overwrite another's
///     values by naming it, and overwrite the base value every tenant inherits by naming none.
/// </summary>
public class TheTenantACallerBelongsToTests
{
    private static readonly ICurrentUser AcmeAdmin = TestCaller.Of("acme");
    private static readonly ICurrentUser Operator = TestCaller.Operator;

    [Fact]
    public async Task ACallerOfOneTenant_CannotWriteAnother()
    {
        var store = new RecordingConfigurationStore();

        var result = await Set(store, AcmeAdmin, tenantId: "globex").Execute();

        result.Error.Should().BeOfType<ForbiddenError>();
        store.SetCalls.Should().BeEmpty();
    }

    /// <summary>The base value is every tenant's default: writing it is writing to all of them.</summary>
    [Fact]
    public async Task ACallerOfOneTenant_CannotWriteTheBaseValue()
    {
        var store = new RecordingConfigurationStore();

        var result = await Set(store, AcmeAdmin, tenantId: null).Execute();

        result.Error.Should().BeOfType<ForbiddenError>();
        store.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ACallerOfOneTenant_WritesItsOwn()
    {
        var store = new RecordingConfigurationStore();

        var result = await Set(store, AcmeAdmin, tenantId: "acme").Execute();

        result.IsSuccess.Should().BeTrue();
        store.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task ACallerWithNoTenant_WritesTheBaseValue()
    {
        var store = new RecordingConfigurationStore();

        var result = await Set(store, Operator, tenantId: null).Execute();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ACallerOfOneTenant_CannotDeleteAnothersValue()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "Globex", "globex");

        var action = Inject(new DeleteConfigValue { Key = "App:Name", TenantId = "globex" }, store, AcmeAdmin);
        var result = await action.Execute();

        result.Error.Should().BeOfType<ForbiddenError>();
        (await store.GetAsync("App:Name", "globex")).Should().Be("Globex");
    }

    [Fact]
    public async Task ACallerOfOneTenant_CannotReadAnothersValue()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Secret", "globex-only", "globex");

        var action = Inject(new GetConfigValue { Key = "App:Secret", TenantId = "globex" }, store, AcmeAdmin);
        var result = await action.Execute();

        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task ACallerOfOneTenant_CannotListAnothersValues()
    {
        var store = new RecordingConfigurationStore();

        var action = Inject(new GetConfigValues { Prefix = "App", TenantId = "globex" }, store, AcmeAdmin);
        var result = await action.Execute();

        result.Error.Should().BeOfType<ForbiddenError>();
    }

    /// <summary>The base value is what the tenant inherits anyway: reading it discloses nothing.</summary>
    [Fact]
    public async Task ACallerOfOneTenant_ReadsTheBaseValue()
    {
        var store = new RecordingConfigurationStore();
        await store.SetAsync("App:Name", "Default");

        var action = Inject(new GetConfigValue { Key = "App:Name" }, store, AcmeAdmin);
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
    }

    private static SetConfigValue Set(IConfigurationStore store, ICurrentUser caller, string? tenantId)
        => ActionFieldInjector.Inject(
            Inject(new SetConfigValue { Key = "App:Name", Value = "v", TenantId = tenantId }, store, caller),
            "_catalog", TestCatalog.App());

    private static T Inject<T>(T action, IConfigurationStore store, ICurrentUser caller)
        => ActionFieldInjector.Inject(ActionFieldInjector.Inject(action, "_store", store), "_currentUser", caller);
}
