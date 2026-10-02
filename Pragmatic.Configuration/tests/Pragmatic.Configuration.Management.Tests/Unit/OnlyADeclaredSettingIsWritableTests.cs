using Pragmatic.Configuration.Discovery;
using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>
///     <c>SetConfigValue</c> writes only a setting the application declares — a property of a
///     <c>[Configuration]</c> section, or a key beneath one — and a key of a sane shape.
/// </summary>
/// <remarks>
///     The action is an HTTP endpoint and wrote any key it was given: <c>IConfigurationStore</c> leaves the
///     key to its caller, and here the caller is the package. A typo became a setting nobody reads, and a
///     key some backends interpret (SQL patterns, Redis globs, paths) went straight to them.
/// </remarks>
public class OnlyADeclaredSettingIsWritableTests
{
    [Theory]
    [InlineData("Booking:MaxStayDays")]
    [InlineData("booking:maxstaydays")]
    [InlineData("Booking:Holidays:0")]
    public async Task ADeclaredSetting_IsWritten(string key)
    {
        var store = new RecordingConfigurationStore();

        var result = await Build(store, key, Catalog()).Execute();

        result.IsSuccess.Should().BeTrue();
        store.SetCalls.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Booking:MaxStay")]
    [InlineData("Payments:ApiKey")]
    [InlineData("Booking")]
    public async Task AKeyNoSectionDeclares_IsRefused(string key)
    {
        var store = new RecordingConfigurationStore();

        var result = await Build(store, key, Catalog()).Execute();

        result.Error.Should().BeOfType<BadRequestError>();
        store.SetCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Booking:MaxStayDays*")]
    [InlineData("Booking:Max StayDays")]
    [InlineData("Booking:MaxStayDays'; --")]
    public async Task AKeyOfAnotherShape_IsRefused(string key)
    {
        var store = new RecordingConfigurationStore();

        var result = await Build(store, key, Catalog()).Execute();

        result.Error.Should().BeOfType<BadRequestError>();
        store.SetCalls.Should().BeEmpty();
    }

    /// <summary>
    ///     An application that declares no section has an empty catalogue, so nothing is writable —
    ///     refused, not guessed.
    /// </summary>
    [Fact]
    public async Task WithAnEmptyCatalogue_NothingIsWritten()
    {
        var store = new RecordingConfigurationStore();

        var result = await Build(store, "Booking:MaxStayDays", new ConfigurationCatalog()).Execute();

        result.Error.Should().BeOfType<BadRequestError>();
        store.SetCalls.Should().BeEmpty();
    }

    internal static ConfigurationCatalog Catalog()
    {
        var catalog = new ConfigurationCatalog();
        catalog.Contribute([
            new ConfigurationSectionDescriptor
            {
                SectionPath = "Booking",
                TypeName = "App.BookingOptions",
                Properties =
                [
                    new ConfigurationPropertyDescriptor { Name = "MaxStayDays", TypeName = "int" },
                    new ConfigurationPropertyDescriptor { Name = "Holidays", TypeName = "System.Collections.Generic.List<string>" },
                ],
            },
        ]);
        return catalog;
    }

    private static SetConfigValue Build(IConfigurationStore store, string key, IConfigurationCatalog catalog)
        => ActionFieldInjector.Inject(ActionFieldInjector.Inject(ActionFieldInjector.Inject(
            new SetConfigValue { Key = key, Value = "30" }, "_store", store), "_currentUser", TestCaller.Operator),
            "_catalog", catalog);
}
