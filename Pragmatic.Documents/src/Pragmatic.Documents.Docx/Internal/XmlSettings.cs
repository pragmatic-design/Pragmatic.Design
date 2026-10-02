using System.Xml;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>Shared XmlWriterSettings for OOXML parts.</summary>
internal static class XmlSettings
{
    internal static XmlWriterSettings Default { get; } = new()
    {
        Encoding = System.Text.Encoding.UTF8,
        CloseOutput = false,
        Indent = false
    };
}
