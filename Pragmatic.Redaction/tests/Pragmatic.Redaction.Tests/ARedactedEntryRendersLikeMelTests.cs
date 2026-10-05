using System.Globalization;
using Microsoft.Extensions.Logging;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Redaction.Tests;

/// <summary>
///     A redacted entry's message is rendered exactly as <c>Microsoft.Extensions.Logging</c> renders it,
///     except for the masked value.
/// </summary>
/// <remarks>
///     <para>
///         The message of an entry that carries a declared value is rendered from the masked state, not
///         by the caller's formatter (#77). Every other placeholder in it must come out as MEL would have
///         written it, or the same call logs differently depending on whether one of its arguments
///         declared something.
///     </para>
///     <para>
///         The expectation is MEL's own rendering, taken from a logger that captures the formatter's
///         result, of the same call with the declared argument replaced by its redacted form. Nothing
///         here is a string written by hand.
///     </para>
/// </remarks>
public class ARedactedEntryRendersLikeMelTests
{
    private sealed record Secret(string Value);

    private sealed class Map : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
        {
            members = type == typeof(Secret) ? [new RedactedMember(nameof(Secret.Value), RedactionReason.NotLogged)] : [];
            return members.Count > 0;
        }
    }

    private static readonly DeclaredRedactor Redactor = new([new Map()]);

    [Fact]
    public void ARepeatedName_TakesEachValueInTurn()
        => RendersLikeMel("{Who}: {N} then {N}", new Secret("s"), 1, 2);

    [Fact]
    public void AnEnumerable_IsJoined()
        => RendersLikeMel("{Who} holds {Items}", new Secret("s"), new[] { 1, 2, 3 });

    [Fact]
    public void ANumber_IsFormattedInvariantly_WhateverTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
        try
        {
            RendersLikeMel("{Who} paid {Amount}", new Secret("s"), 1234.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The items of an enumerable, under a culture whose decimal separator is not a dot.</summary>
    [Fact]
    public void TheItemsOfAnEnumerable_AreFormattedAsMelFormatsThem()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
        try
        {
            RendersLikeMel("{Who} split {Amounts}", new Secret("s"), new[] { 1.5m, 2.25m });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Null_IsWrittenAsMelWritesIt()
        => RendersLikeMel("{Who} said {Text}", new Secret("s"), (string?)null);

    [Fact]
    public void AlignmentAndFormat_AreHonoured()
        => RendersLikeMel("{Who} [{Amount,10:F2}] [{Count,-4}]", new Secret("s"), 3.14159, 7);

    [Fact]
    public void EscapedBraces_StayLiteral()
        => RendersLikeMel("{{literal}} {Who} {{and}} {N}", new Secret("s"), 5);

    /// <summary>
    ///     Logs the call through a capturing MEL logger twice: once as written, which goes through
    ///     <see cref="DeclaredRedactor.RedactState" />, and once with every declared argument already
    ///     replaced by its redacted form, which MEL renders itself. The two messages must be equal.
    /// </summary>
    private static void RendersLikeMel(string template, params object?[] args)
    {
        var capture = new CapturingLogger();

        capture.LogInformation(template, args);
        var redacted = Redactor.RedactState(capture.LastState!);
        redacted.Should().NotBeNull("the call carries a declared value, so it is redacted");

        var substituted = args.Select(Redactor.RedactValue).ToArray();
        capture.LogInformation(template, substituted);
        var melRendering = capture.LastMessage;

        redacted!.ToString().Should().Be(melRendering);
    }

    private sealed class CapturingLogger : ILogger
    {
        public IReadOnlyList<KeyValuePair<string, object?>>? LastState { get; private set; }

        public string? LastMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LastState = state as IReadOnlyList<KeyValuePair<string, object?>>;
            LastMessage = formatter(state, exception);
        }
    }
}
