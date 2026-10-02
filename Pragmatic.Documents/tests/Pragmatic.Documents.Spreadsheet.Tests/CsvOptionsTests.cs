using System.Globalization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Spreadsheet.Tests;

public class CsvOptionsTests
{
    [Fact]
    public void Default_HasCommaDelimiter()
    {
        var opts = CsvOptions.Default;

        opts.Delimiter.Should().Be(',');
        opts.Culture.Should().Be(CultureInfo.InvariantCulture);
        opts.DateFormat.Should().Be("yyyy-MM-dd");
        opts.HasHeaders.Should().BeTrue();
    }

    [Fact]
    public void Italian_HasSemicolonDelimiter()
    {
        var opts = CsvOptions.Italian;

        opts.Delimiter.Should().Be(';');
        opts.Culture.Name.Should().Be("it-IT");
        opts.DateFormat.Should().Be("dd/MM/yyyy");
    }

    [Fact]
    public void Custom_Options()
    {
        var opts = new CsvOptions
        {
            Delimiter = '\t',
            Culture = CultureInfo.GetCultureInfo("de-DE"),
            DateFormat = "dd.MM.yyyy",
            HasHeaders = false
        };

        opts.Delimiter.Should().Be('\t');
        opts.Culture.Name.Should().Be("de-DE");
        opts.HasHeaders.Should().BeFalse();
    }
}
