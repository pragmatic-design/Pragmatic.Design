using System.Text;
using Casework.Intake.Infrastructure.Letters;
using Pragmatic.Documents.Model;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     Every piece of text a letter holds, flattened.
/// </summary>
/// <remarks>
///     This is what "assert on the model" looks like: the letter's words, out of the nodes, with no
///     renderer in between. A PDF would have compressed them.
/// </remarks>
internal static class LetterText
{
    public static string TextOf(WrittenLetter letter) => TextOf(letter.Model);

    public static string TextOf(DocumentModel model)
    {
        var text = new StringBuilder();

        foreach (var page in model.Pages)
            foreach (var node in page.Content)
                Flatten(node, text);

        return text.ToString();
    }

    private static void Flatten(DocumentNode node, StringBuilder into)
    {
        switch (node)
        {
            case TextNode text:
                into.AppendLine(text.Content);
                break;
            case HeadingNode heading:
                into.AppendLine(heading.Content);
                break;
            case ContainerNode container:
                foreach (var child in container.Children)
                    Flatten(child, into);
                break;
        }
    }
}
