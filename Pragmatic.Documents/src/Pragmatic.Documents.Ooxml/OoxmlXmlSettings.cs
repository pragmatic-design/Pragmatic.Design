using System.Xml;

namespace Pragmatic.Documents.Ooxml;

/// <summary>Shared XmlWriterSettings for OOXML parts.</summary>
public static class OoxmlXmlSettings
{
    public static XmlWriterSettings Default { get; } = new()
    {
        Encoding = System.Text.Encoding.UTF8,
        CloseOutput = false,
        Indent = false
    };
}
