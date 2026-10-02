namespace Pragmatic.Documents.Templating.Pipes;

/// <summary>Built-in template pipes: number, uppercase, lowercase, trim, default.</summary>
/// <remarks>
///     ⚠️ <b>Not the whole set an application can have, and this list read as if it were.</b>
///     <c>date</c>, <c>currency</c> and <c>percent</c> are in <c>Pragmatic.Documents.Templating.I18N</c>
///     and are added with <c>PipeRegistry.Default.WithI18N()</c> — they format with the data context's
///     culture, which the obvious hand-written pipe does not. An application read these five, concluded
///     there was no date formatting and wrote its own; <c>PipeRegistry</c>'s "unknown pipe"
///     message now points at the package, because a closed list is read as a closed set.
/// </remarks>
internal static class BuiltInPipes
{
    public static IReadOnlyList<ITemplatePipe> All =>
    [
        new NumberPipe(),
        new UppercasePipe(),
        new LowercasePipe(),
        new TrimPipe(),
        new DefaultPipe()
    ];
}
