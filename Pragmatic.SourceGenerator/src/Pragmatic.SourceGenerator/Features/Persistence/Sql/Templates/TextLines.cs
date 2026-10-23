using System.Collections.Generic;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>The lines of a text written elsewhere, for a template to append one by one.</summary>
internal static class TextLines
{
    /// <summary>Every line, the blank ones kept; the newline that ends the text opens no line, and an empty text has none.</summary>
    public static IEnumerable<string> Of(string text)
    {
        if (text.Length == 0)
            yield break;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var count = text.EndsWith("\n") ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
            yield return lines[i];
    }
}
