using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Robustness of the shared template base: XML doc escaping, identifier escaping, and the file
///     header. Each test drives the real emitters (no reflection into privates) and, where the defect
///     was "the generated code does not compile", parses the result with Roslyn to prove it.
/// </summary>
public sealed class CSharpTemplateTests
{
    // =========================================================================
    // XML doc tags
    // =========================================================================

    [Fact]
    public void XmlSummary_StandardDocTags_ArePreserved()
    {
        var summary = "Uses <list type=\"bullet\"><item><description>one</description></item></list>"
                      + " and <para>a paragraph</para> with <code>x = 1;</code>.";

        var rendered = Render(t => t.Summary(summary));

        rendered.Should().Contain("<list type=\"bullet\">")
            .And.Contain("<item>").And.Contain("</item>")
            .And.Contain("<description>").And.Contain("</description>")
            .And.Contain("</list>")
            .And.Contain("<para>").And.Contain("</para>")
            .And.Contain("<code>").And.Contain("</code>");
        rendered.Should().NotContain("&lt;list").And.NotContain("&lt;para").And.NotContain("&lt;code");
    }

    [Theory]
    [InlineData("see")]
    [InlineData("seealso")]
    [InlineData("paramref")]
    [InlineData("typeparamref")]
    [InlineData("c")]
    [InlineData("code")]
    [InlineData("example")]
    [InlineData("returns")]
    [InlineData("value")]
    [InlineData("para")]
    [InlineData("list")]
    [InlineData("item")]
    [InlineData("term")]
    [InlineData("description")]
    [InlineData("remarks")]
    [InlineData("exception")]
    [InlineData("note")]
    public void XmlSummary_KnownTag_IsNotEscaped(string tagName)
    {
        var rendered = Render(t => t.Summary($"before <{tagName}>body</{tagName}> after"));

        rendered.Should().Contain($"<{tagName}>").And.Contain($"</{tagName}>");
        rendered.Should().NotContain("&lt;");
    }

    [Theory]
    [InlineData("<see cref=\"Order\"/>")]
    [InlineData("<paramref name=\"id\"/>")]
    [InlineData("<br/>")]
    [InlineData("<inheritdoc/>")]
    public void XmlSummary_SelfClosingKnownTag_IsNotEscaped(string tag)
        => Render(t => t.Summary($"text {tag} text")).Should().Contain(tag);

    [Theory]
    // Generic type names look like tags but are not: escaping them is the whole point.
    [InlineData("List<string>", "List&lt;string&gt;")]
    [InlineData("IReadOnlyList<T>", "IReadOnlyList&lt;T&gt;")]
    [InlineData("Dictionary<string, int>", "Dictionary&lt;string, int&gt;")]
    [InlineData("a < b && b > c", "a &lt; b &amp;&amp; b &gt; c")]
    [InlineData("<notatag>", "&lt;notatag&gt;")]
    public void XmlSummary_NonTagAngleBrackets_AreEscaped(string input, string expected)
        => Render(t => t.Summary(input)).Should().Contain(expected);

    [Fact]
    public void XmlSummary_PreservedTags_StillProduceWellFormedXml()
    {
        // Guards the new risk introduced by preserving more tags: the doc comment must still parse.
        // (Note: the old behaviour was not malformed XML — `&lt;list&gt;` is well-formed — it silently
        // downgraded the markup to literal text, which is what XmlSummary_StandardDocTags_ArePreserved pins.)
        var source = Render(t =>
        {
            t.Summary("Groups the rows.<para>Modes:</para><list type=\"bullet\">"
                      + "<item><description>fast</description></item></list>");
            t.Line("public sealed class Grouper { }");
        });

        DocCommentDiagnostics(source).Should().BeEmpty();
    }

    // =========================================================================
    // Keyword-named identifiers
    // =========================================================================

    [Theory]
    [InlineData("Event", "@event")]
    [InlineData("event", "@event")]
    [InlineData("Class", "@class")]
    [InlineData("class", "@class")]
    [InlineData("Default", "@default")]
    [InlineData("Lock", "@lock")]
    [InlineData("Object", "@object")]
    [InlineData("String", "@string")]
    public void MethodParameter_KeywordName_IsEscaped(string name, string expected)
        => Probe.Parameter("int", name).Should().Be($"int {expected}");

    [Theory]
    [InlineData("OrderId", "orderId")]
    [InlineData("EventName", "eventName")]
    [InlineData("Value", "value")] // contextual keyword, legal as a parameter name
    public void MethodParameter_NonKeywordName_IsUnchanged(string name, string expected)
        => Probe.Parameter("string", name).Should().Be($"string {expected}");

    [Fact]
    public void MethodParameter_KeywordName_KeepsModifiersAndDefault()
        => Probe.ParameterWithDefault("bool", "Default", "false").Should().Be("bool @default = false");

    [Fact]
    public void Method_WithKeywordNamedParameters_ProducesCompilableSource()
    {
        var source = Render(t =>
        {
            t.Line("public class Handler");
            t.Line("{");
            t.EmitMethod("Handle", "void", ("int", "event"), ("string", "class"));
            t.Line("}");
        });

        source.Should().Contain("@event").And.Contain("@class");
        SyntaxDiagnostics(source).Should().BeEmpty();
    }

    [Fact]
    public void MemberNames_ThatAreKeywords_AreEscaped()
    {
        // A user type declared as `class @class` reports SymbolName "class"; every emitter that puts a
        // caller-supplied name straight into the output has the same hazard as the parameter one.
        var source = Render(t => t.EmitKeywordNamedMembers());

        source.Should().Contain("class @class").And.Contain("int @event").And.Contain("void @lock(");
        SyntaxDiagnostics(source).Should().BeEmpty();
    }

    // =========================================================================
    // Header
    // =========================================================================

    [Fact]
    public void RenderHeader_NamesTheFrameworkAndItsAuthor_AndLeavesTheFileToTheProject()
    {
        var rendered = Render(t => t.Line("// body"));

        Lines(rendered).Take(4).Should().Equal(
            "// <auto-generated/>",
            "// Generated by Pragmatic.Design, a framework by Alessandro Saiani",
            "// https://pragmaticdesign.net",
            "// This file belongs to your project; no rights are claimed in it.");
    }

    /// <summary>
    ///     The file lands in the consumer's project, and the licensing promise is that it is theirs. A
    ///     copyright line or a licence name at its top says the opposite to anyone who reads it — and to
    ///     every licence scanner — whatever it was meant to refer to.
    /// </summary>
    [Fact]
    public void RenderHeader_ClaimsNoCopyrightAndNoLicence()
    {
        var rendered = Render(t => t.Line("// body"));

        rendered.Should().NotContain("Copyright")
            .And.NotContain("All rights reserved")
            .And.NotContain("License");
    }

    [Fact]
    public void RenderHeader_NamesTheTool_Once()
    {
        var rendered = new Probe(t => t.Line("// body"), "Pragmatic.SourceGenerator/Probe").ToString()!;

        Lines(rendered)[4].Should().StartWith("// Pragmatic.SourceGenerator/Probe v");
        Lines(rendered).Count(l => l.StartsWith("// Generated by", StringComparison.Ordinal)).Should().Be(1,
            "the attribution already says what generated the file; the tool line only names the feature");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string Render(Action<Probe> body) => new Probe(body).ToString()!;

    private static string[] Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    private static IEnumerable<Diagnostic> SyntaxDiagnostics(string source)
        => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);

    private static IEnumerable<Diagnostic> DocCommentDiagnostics(string source)
        => CSharpSyntaxTree
            .ParseText(source,
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose))
            .GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);

    /// <summary>Minimal concrete template that exposes the protected emitters to the tests.</summary>
    private sealed class Probe(Action<Probe> body, string? generatorName = null) : CSharpTemplate
    {
        protected override string? GeneratorName => generatorName;

        public override Artifact RenderOutput() => new("Probe.g.cs", ToSourceText());

        public override void RenderFile() => body(this);

        public void Summary(string text) => XmlSummary(text);

        public void Line(string text) => AppendLine(text);

        public void EmitMethod(string name, string returnType, params (string Type, string Name)[] parameters)
            => Method(name, () => { }, returnType,
                parameters.Select(p => new MethodParameter(p.Type, p.Name)).ToList());

        public void EmitKeywordNamedMembers()
            => Class("class", () =>
            {
                Property("event", "int");
                Field("lock", "object");
                Method("lock", () => { }, "void");
            }, modifiers: new ClassModifiers { Sealed = true });

        public static string Parameter(string type, string name)
            => new MethodParameter(type, name).ToString();

        public static string ParameterWithDefault(string type, string name, string defaultValue)
            => new MethodParameter(type, name) { DefaultValue = defaultValue }.ToString();
    }
}
