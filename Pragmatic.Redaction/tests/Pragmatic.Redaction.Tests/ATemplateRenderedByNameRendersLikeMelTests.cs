using System.Globalization;
using Microsoft.Extensions.Logging;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Redaction.Tests;

/// <summary>
///     <see cref="LogMessageTemplate.Render(string, IReadOnlyDictionary{string, object?})" />, which takes the
///     values by name, renders what <c>Microsoft.Extensions.Logging</c> renders for the same call.
/// </summary>
/// <remarks>
///     Pragmatic.Logging re-renders a message from an entry's properties after masking one by its name, and
///     there the values are in a dictionary, not in the call's order. The expectation is MEL's own rendering
///     of the call, captured from its formatter; the dictionary is built from the same state.
/// </remarks>
public class ATemplateRenderedByNameRendersLikeMelTests
{
    [Fact]
    public void Values_TakeTheirPlaceByName()
        => RendersLikeMel("{Who} paid {Amount} for {Order}", "Ada", 12.5m, "ORD-7");

    [Fact]
    public void AlignmentAndFormat_AreHonoured()
        => RendersLikeMel("[{Amount,10:F2}] [{Count,-4}]", 3.14159, 7);

    [Fact]
    public void EscapedBraces_StayLiteral()
        => RendersLikeMel("{{literal}} {N} {{and}}", 5);

    [Fact]
    public void AnEnumerable_IsJoined()
        => RendersLikeMel("{Who} holds {Items}", "Ada", new[] { 1, 2, 3 });

    [Fact]
    public void Null_IsWrittenAsMelWritesIt()
        => RendersLikeMel("{Who} said {Text}", "Ada", (string?)null);

    [Fact]
    public void ANumber_IsFormattedInvariantly_WhateverTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
        try
        {
            RendersLikeMel("{Who} paid {Amount}", "Ada", 1234.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ANameWithNoValue_IsWrittenAsNull()
        => LogMessageTemplate.Render("{Who} paid {Amount}", new Dictionary<string, object?> { ["Who"] = "Ada" })
            .Should().Be("Ada paid (null)");

    private static void RendersLikeMel(string template, params object?[] args)
    {
        var capture = new CapturingLogger();
        capture.LogInformation(template, args);

        var byName = capture.LastState!
            .Where(v => v.Key != "{OriginalFormat}")
            .ToDictionary(v => v.Key, v => v.Value);

        LogMessageTemplate.Render(template, byName).Should().Be(capture.LastMessage);
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
