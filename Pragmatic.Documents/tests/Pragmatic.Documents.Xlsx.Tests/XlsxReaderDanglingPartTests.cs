using System.IO.Compression;
using System.Xml;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>
///     A sheet the workbook declares is read or the package is refused (#308). A declaration that leads to no
///     worksheet part is a damaged package, and reading it as a sheet with no rows, or as no sheet at all, would hand an
///     import an empty table where the file had data.
/// </summary>
public class XlsxReaderDanglingPartTests
{
    private const string Sheet = "<sheetData><row><c><v>1</v></c></row></sheetData>";

    [Fact]
    public void Read_SheetWhosePartIsMissing_IsRefused()
    {
        var package = XlsxPackages.Package(new Dictionary<string, string>
        {
            ["xl/workbook.xml"] = XlsxPackages.Workbook("<sheet name=\"A\" r:id=\"rId1\"/>"),
            ["xl/_rels/workbook.xml.rels"] = XlsxPackages.WorkbookRelationships(
                "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>"),
        });

        var act = () => XlsxReader.Read(package);

        act.Should().Throw<InvalidOperationException>().WithMessage("*xl/worksheets/sheet1.xml*");
    }

    [Fact]
    public void Read_SheetWhoseRelationshipIsMissing_IsRefused()
    {
        var package = XlsxPackages.Package(new Dictionary<string, string>
        {
            ["xl/workbook.xml"] = XlsxPackages.Workbook("<sheet name=\"A\" r:id=\"rId2\"/>"),
            ["xl/_rels/workbook.xml.rels"] = XlsxPackages.WorkbookRelationships(
                "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>"),
            ["xl/worksheets/sheet1.xml"] = XlsxPackages.Worksheet(Sheet),
        });

        var act = () => XlsxReader.Read(package);

        act.Should().Throw<InvalidOperationException>().WithMessage("*rId2*");
    }

    [Fact]
    public void Read_SheetWithoutARelationship_IsRefused()
    {
        var package = XlsxPackages.Package(new Dictionary<string, string>
        {
            ["xl/workbook.xml"] = XlsxPackages.Workbook("<sheet name=\"A\"/>"),
            ["xl/_rels/workbook.xml.rels"] = XlsxPackages.WorkbookRelationships(
                "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>"),
            ["xl/worksheets/sheet1.xml"] = XlsxPackages.Worksheet(Sheet),
        });

        var act = () => XlsxReader.Read(package);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'A'*");
    }

    [Fact]
    public void Read_SheetWithItsPart_IsRead()
    {
        var package = XlsxPackages.Package(new Dictionary<string, string>
        {
            ["xl/workbook.xml"] = XlsxPackages.Workbook("<sheet name=\"A\" r:id=\"rId1\"/>"),
            ["xl/_rels/workbook.xml.rels"] = XlsxPackages.WorkbookRelationships(
                "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>"),
            ["xl/worksheets/sheet1.xml"] = XlsxPackages.Worksheet(Sheet),
        });

        XlsxReader.Read(package).Sheets.Single().Rows.Single().Cells.Single().Value.Should().Be(1);
    }

    /// <summary>
    ///     The size guard reads the size the archive declares. An entry that declares 1 KB and inflates to 128 MB, past
    ///     the 100 MB limit, must not be read to its end: the reader stops where the declaration does.
    /// </summary>
    [Fact]
    public void Read_EntryThatInflatesPastItsDeclaredSize_IsRefused()
    {
        var whitespace = new string(' ', 128 * 1024 * 1024);
        var package = XlsxPackages.Package(new Dictionary<string, string>
        {
            ["xl/workbook.xml"] = XlsxPackages.Workbook("<sheet name=\"A\" r:id=\"rId1\"/>"),
            ["xl/_rels/workbook.xml.rels"] = XlsxPackages.WorkbookRelationships(
                "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>"),
            ["xl/worksheets/sheet1.xml"] = XlsxPackages.Worksheet(whitespace + Sheet),
        }, CompressionLevel.Optimal);
        var lying = XlsxPackages.DeclareUncompressedSize(package, "xl/worksheets/sheet1.xml", 1024);

        var act = () => XlsxReader.Read(lying);

        act.Should().Throw<Exception>().Where(e => e is InvalidDataException or XmlException);
    }
}
