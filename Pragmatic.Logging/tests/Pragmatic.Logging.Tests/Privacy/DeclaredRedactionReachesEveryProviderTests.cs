using Pragmatic.Logging.Providers;
using Pragmatic.Redaction;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
///     Declared redaction is applied to the entry, once, before any provider formats it.
///     <para>
///         Redacting in the two JSON providers alone would leave the console writer, which does
///         <c>Append(kvp.Value)</c>, and the debug writer, which falls through to <c>ToString()</c> —
///         and a record's <c>ToString()</c> prints every member. Per-provider redaction means the next
///         provider leaks and nobody notices.
///     </para>
/// </summary>
/// <remarks>
///     ⚠️ These entries are built by hand, with the template as the message, so the value never reaches
///     the text and the message is not what they test. They cover the properties path, which an entry
///     takes however it was produced. The message is covered through a real logger in
///     <see cref="DeclaredRedactionReachesTheMessageTests" />; until those existed, a message carrying
///     the member in clear passed every test here.
/// </remarks>
public class DeclaredRedactionReachesEveryProviderTests
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

    private static LogEntry EntryWith(Payload payload) => new()
    {
        Timestamp = DateTimeOffset.UnixEpoch.UtcDateTime,
        LogLevel = Microsoft.Extensions.Logging.LogLevel.Information,
        Category = "Test",
        Message = "handling {Payload}",
        Properties = new Dictionary<string, object?> { ["Payload"] = payload },
    };

    private static PragmaticMemoryProvider ProviderWithRedaction(bool enablePatternRedaction)
    {
        var config = new PragmaticProviderConfiguration();
        config.Privacy.EnableRedaction = enablePatternRedaction;

        return new PragmaticMemoryProvider("memory", config)
        {
            DeclaredRedactor = new DeclaredRedactor([new Map()]),
        };
    }

    /// <summary>
    ///     The load-bearing one: the pattern heuristic is OFF, as the development preset leaves it,
    ///     and the declared member is still masked.
    /// </summary>
    [Fact]
    public void WithPatternRedactionOff_TheDeclaredMemberIsStillMasked()
    {
        using var provider = ProviderWithRedaction(enablePatternRedaction: false);

        provider.WriteLog(EntryWith(new Payload("PO-1", "sk-live-secret")));

        provider.GetLogEntries().Single().Properties["Payload"]!.ToString()
            .Should().NotContain("sk-live-secret").And.Contain(PersonalDataPatterns.Mask);
    }

    [Fact]
    public void WhatWasNotDeclared_Survives()
    {
        using var provider = ProviderWithRedaction(enablePatternRedaction: false);

        provider.WriteLog(EntryWith(new Payload("PO-1", "sk-live-secret")));

        provider.GetLogEntries().Single().Properties["Payload"]!.ToString().Should().Contain("PO-1");
    }

    /// <summary>
    ///     The value the provider receives is already a string, so a writer that calls
    ///     <c>ToString()</c> — console, debug — cannot reach the original graph even by accident.
    /// </summary>
    [Fact]
    public void TheProviderNeverSeesTheOriginalObject()
    {
        using var provider = ProviderWithRedaction(enablePatternRedaction: false);

        provider.WriteLog(EntryWith(new Payload("PO-1", "sk-live-secret")));

        provider.GetLogEntries().Single().Properties["Payload"].Should().BeOfType<string>();
    }

    [Fact]
    public void WithNoRedactorAtAll_NothingChanges()
    {
        var config = new PragmaticProviderConfiguration();
        config.Privacy.EnableRedaction = false;
        using var provider = new PragmaticMemoryProvider("memory", config);

        provider.WriteLog(EntryWith(new Payload("PO-1", "sk-live-secret")));

        provider.GetLogEntries().Single().Properties["Payload"].Should().BeOfType<Payload>();
    }
}
