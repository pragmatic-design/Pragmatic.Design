using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Markup;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
/// Security posture of the markup parsers: DTDs are prohibited (blocks XXE / billion-laughs) and
/// malformed input surfaces as an exception rather than silently succeeding.
/// </summary>
public class MarkupSecurityTests
{
    [Fact]
    public void PdxDocParser_WithDtd_IsRejected()
    {
        // A DOCTYPE/DTD is the vector for XXE and internal-entity (billion-laughs) expansion.
        // DtdProcessing.Prohibit must reject it outright.
        const string markup = """
            <?xml version="1.0"?>
            <!DOCTYPE document [ <!ENTITY x "expanded"> ]>
            <document><page><text>&x;</text></page></document>
            """;

        var act = () => PdxDocParser.Parse(markup);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void PdxEmailParser_WithDtd_IsRejected()
    {
        const string markup = """
            <?xml version="1.0"?>
            <!DOCTYPE email [ <!ENTITY x "expanded"> ]>
            <email><text>&x;</text></email>
            """;

        var act = () => PdxEmailParser.Parse(markup);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void PdxDocParser_WithExternalEntity_DoesNotResolveIt()
    {
        // An external entity must never be fetched (XmlResolver = null). With DTDs prohibited this
        // throws before any resolution could happen.
        const string markup = """
            <?xml version="1.0"?>
            <!DOCTYPE document [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <document><page><text>&xxe;</text></page></document>
            """;

        var act = () => PdxDocParser.Parse(markup);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void PdxDocParser_WrongRoot_ThrowsMarkupParseException()
    {
        var act = () => PdxDocParser.Parse("<notdocument></notdocument>");

        act.Should().Throw<MarkupParseException>();
    }

    [Fact]
    public void PdxDocParser_MalformedXml_Throws()
    {
        var act = () => PdxDocParser.Parse("<document><page><text>unclosed");

        act.Should().Throw<Exception>();
    }
}
