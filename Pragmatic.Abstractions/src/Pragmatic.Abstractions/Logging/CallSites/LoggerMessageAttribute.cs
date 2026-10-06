using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.CallSites;

/// <summary>
///     Declares a log call site whose body the Pragmatic source generator writes: the same name, the
///     same constructors and the same properties as <c>Microsoft.Extensions.Logging.LoggerMessageAttribute</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>How a project gets this one rather than Microsoft's.</b> The generator's package declares a
///         global using alias, <c>LoggerMessageAttribute = global::Pragmatic.Logging.CallSites.LoggerMessageAttribute</c>,
///         through MSBuild. An existing <c>[LoggerMessage]</c> then binds here without a source change,
///         and Microsoft's generator, which looks for its own attribute, stays silent. The alias is
///         required, not a convenience: a file that logs imports <c>Microsoft.Extensions.Logging</c> for
///         <c>ILogger</c>, and two attributes of the same name imported by namespace are ambiguous.
///     </para>
///     <para>
///         <b>What it adds over Microsoft's.</b> The generated state reaches the provider as a
///         <c>readonly struct</c> that writes itself as UTF-8 (<see cref="IUtf8LogState" />) without
///         boxing; an argument whose parameter is marked <c>[NotLogged]</c> or <c>[PersonalData]</c> is
///         written as the mask by the generated code itself, in every view of the state; and the
///         Pragmatic generator can declare one inside code it generates, which Microsoft's cannot see.
///     </para>
///     <para>
///         <b>Opting out.</b> Writing <c>[Microsoft.Extensions.Logging.LoggerMessage]</c> fully qualified
///         hands that method to Microsoft's generator, deliberately.
///     </para>
///     <para>
///         ⚠️ The type is in <c>Pragmatic.Logging.CallSites</c>, not in <c>Pragmatic.Logging</c>, on
///         purpose: C# looks up a name in the namespaces that enclose a file before it looks at the
///         file's using directives, so a type of this name in <c>Pragmatic.Logging</c> would have taken
///         every <c>[LoggerMessage]</c> written in a <c>Pragmatic.Logging.*</c> namespace, alias or not.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LoggerMessageAttribute : Attribute
{
    /// <summary>A call site whose event id, level and message are set through the properties.</summary>
    public LoggerMessageAttribute()
    {
    }

    /// <summary>A call site with an event id, a level and a message.</summary>
    public LoggerMessageAttribute(int eventId, LogLevel level, string message)
    {
        EventId = eventId;
        Level = level;
        Message = message;
    }

    /// <summary>A call site with a level and a message, and an event id derived from its name.</summary>
    public LoggerMessageAttribute(LogLevel level, string message)
    {
        Level = level;
        Message = message;
    }

    /// <summary>A call site with a level and no message.</summary>
    public LoggerMessageAttribute(LogLevel level) => Level = level;

    /// <summary>A call site with a message, whose level is one of the method's parameters.</summary>
    public LoggerMessageAttribute(string message) => Message = message;

    /// <summary>
    ///     The event id; when left at <c>-1</c>, the generator derives a stable one from the event name.
    /// </summary>
    public int EventId { get; set; } = -1;

    /// <summary>The event name; the method's name when not set.</summary>
    public string? EventName { get; set; }

    /// <summary>
    ///     The level; when left at <see cref="LogLevel.None" />, the method takes the level as a parameter.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.None;

    /// <summary>The message template, with <c>{Placeholder}</c> names matching the parameters.</summary>
    public string Message { get; set; } = "";

    /// <summary>Whether the generated body skips the <c>IsEnabled</c> check.</summary>
    public bool SkipEnabledCheck { get; set; }
}
