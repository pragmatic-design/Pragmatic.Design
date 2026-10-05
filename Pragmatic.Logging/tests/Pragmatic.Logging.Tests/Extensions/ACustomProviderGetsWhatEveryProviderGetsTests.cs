using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Redaction;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Extensions;

/// <summary>
///     A provider added with <c>AddProvider&lt;TProvider&gt;(factory)</c> gets the declared redactor and the
///     application's JSON seam, as a built-in provider does.
/// </summary>
/// <remarks>
///     <para>
///         The built-in <c>Add*</c> methods go through the overload of <c>AddPragmaticProvider</c> that
///         attaches both. <c>AddProvider&lt;TProvider&gt;</c>, the documented way to add a custom provider,
///         went through the one that attached nothing. Outside the composed host nothing else masks the
///         declared members, so they went out in clear through that provider.
///     </para>
///     <para>
///         The control is <c>AddConsole()</c>, which has always had both.
///     </para>
/// </remarks>
public class ACustomProviderGetsWhatEveryProviderGetsTests
{
    private sealed record Payload(string Reference, string ApiKey);

    private sealed class Map : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
        {
            members = type == typeof(Payload)
                ? [new RedactedMember(nameof(Payload.ApiKey), RedactionReason.NotLogged)]
                : [];
            return members.Count > 0;
        }
    }

    private static ServiceProvider Build(Action<PragmaticLoggingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRedactionMap, Map>();
        services.AddPragmaticJson();
        services.AddPragmaticLoggingBuilder(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddProvider_TheCustomProviderMasksTheDeclaredMember()
    {
        using var services = Build(logging => logging.AddProvider(_ =>
            new PragmaticMemoryProvider("memory", PragmaticMemoryConfiguration.ForTesting())));
        var provider = services.GetRequiredService<PragmaticMemoryProvider>();

        provider.WriteLog(new LogEntry
        {
            Timestamp = DateTimeOffset.UnixEpoch.UtcDateTime,
            LogLevel = Microsoft.Extensions.Logging.LogLevel.Information,
            Category = "Test",
            Message = "handling {Payload}",
            Properties = new Dictionary<string, object?> { ["Payload"] = new Payload("PO-1", "sk-live-secret") },
        });

        provider.GetLogEntries().Single().Properties["Payload"]!.ToString()
            .Should().NotContain("sk-live-secret").And.Contain("PO-1");
    }

    [Fact]
    public void AddProvider_TheCustomProviderHasTheApplicationsJsonSeam()
    {
        using var services = Build(logging => logging.AddProvider(_ =>
            new PragmaticMemoryProvider("memory", PragmaticMemoryConfiguration.ForTesting())));

        services.GetRequiredService<PragmaticMemoryProvider>().JsonOptions
            .Should().BeSameAs(services.GetRequiredService<PragmaticJsonOptions>());
    }

    [Fact]
    public void AddConsole_TheBuiltInProviderHasBoth()
    {
        using var services = Build(logging => logging.AddConsole());
        var console = services.GetRequiredService<PragmaticConsoleProvider>();

        console.DeclaredRedactor.Should().NotBeNull();
        console.JsonOptions.Should().BeSameAs(services.GetRequiredService<PragmaticJsonOptions>());
    }
}
