using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

/// <summary>
///     What a validation failure looks like on the wire.
/// </summary>
/// <remarks>
///     <para>
///         <c>ValidationError</c> is a struct implementing <see cref="IError" />, not a subclass of
///         <c>Error</c>. A ProblemDetails mapper that collects extensions behind
///         <c>if (error is Error record)</c> drops the issues — the only part that names the offending
///         field — from the response. A client receives <c>{"code":"VALIDATION_ERROR"}</c>
///         and has to guess.
///     </para>
///     <para>
///         The projection is asserted here, on the type, rather than only end to end: the mapper
///         calls it through <see cref="IError" />, and a test that goes through HTTP would still pass
///         if someone re-narrowed the call to <c>Error</c> while a second path kept working.
///     </para>
/// </remarks>
public class ValidationErrorWireFormatTests
{
    private static Dictionary<string, object?> ExtensionsOf(ValidationError error)
    {
        var extensions = new Dictionary<string, object?>();
        // Deliberately through the interface: that is how the serialization boundary sees it.
        ((IError)error).WriteExtensions(extensions);
        return extensions;
    }

    /// <summary>
    ///     Each issue is published under the name the caller knows, not the one we declared.
    /// </summary>
    /// <remarks>
    ///     ⚠️ These keys are not the C# property names, because the payload uses others: a client
    ///     generated from the published contract looks up <c>title</c>, and under <c>Title</c> the
    ///     field it should highlight would stay unmarked. The convention is applied here
    ///     because this side is the only one that knows it; an explicit rename travels on the issue
    ///     instead, because only the generator sees it.
    /// </remarks>
    [Fact]
    public void WriteExtensions_ProjectsEachIssueUnderTheNameItTravelsUnder()
    {
        var error = ValidationError.FromIssues([
            ValidationIssue.For("Title", "validation.required"),
            ValidationIssue.For("Year", "validation.future")
        ]);

        var errors = ExtensionsOf(error)["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;

        errors.Should().ContainKey("title");
        errors["title"].Should().BeEquivalentTo(["validation.required"]);
        errors.Should().ContainKey("year");
        errors["year"].Should().BeEquivalentTo(["validation.future"]);
    }

    /// <summary>
    ///     With a resolver, each issue's message travels in the caller's language, beside
    ///     its key: <c>errors</c> is what a client matches on, <c>messages</c> what it shows.
    /// </summary>
    /// <remarks>
    ///     The keys were the whole response. Nothing turned an issue's key into words — the resolver
    ///     localized the title and the detail of the error and never saw the issues — while the
    ///     documentation said they were resolved at the serialization boundary.
    /// </remarks>
    [Fact]
    public void WithAResolver_TheMessagesTravelBesideTheKeys()
    {
        var error = ValidationError.FromIssues([
            ValidationIssue.For("To", "validation.ends_before_it_starts"),
            ValidationIssue.For("Name", "validation.minlength", ("min", 3))
        ]);

        var extensions = new Dictionary<string, object?>();
        ((IError)error).WriteExtensions(extensions, new Glossary());

        var errors = extensions["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        errors["to"].Should().Equal("validation.ends_before_it_starts");
        var messages = extensions["messages"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        messages["to"].Should().Equal("The end comes before the start");
        messages["name"].Should().Equal("At least 3 characters");
    }

    /// <summary>
    ///     A key the resolver does not know keeps its place, as the key: the two maps stay aligned, the
    ///     n-th message of a field being the n-th key's.
    /// </summary>
    [Fact]
    public void AKeyTheResolverDoesNotKnow_KeepsItsPlace()
    {
        var error = ValidationError.FromIssues([
            ValidationIssue.For("To", "validation.unheard_of"),
            ValidationIssue.For("To", "validation.ends_before_it_starts")
        ]);

        var extensions = new Dictionary<string, object?>();
        ((IError)error).WriteExtensions(extensions, new Glossary());

        var messages = extensions["messages"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        messages["to"].Should().Equal("validation.unheard_of", "The end comes before the start");
    }

    /// <summary>The control: a resolver that knows nothing adds nothing, and the keys are as they were.</summary>
    [Fact]
    public void AResolverThatKnowsNothing_AddsNoMessages()
    {
        var error = ValidationError.For("To", "validation.ends_before_it_starts");

        var extensions = new Dictionary<string, object?>();
        ((IError)error).WriteExtensions(extensions, new SilentResolver());

        extensions.Should().NotContainKey("messages");
        extensions.Should().ContainKey("errors");
    }

    private sealed class Glossary : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null) => null;

        string? IErrorMessageResolver.ResolveKey(string messageKey, IReadOnlyDictionary<string, object>? parameters)
            => messageKey switch
            {
                "validation.ends_before_it_starts" => "The end comes before the start",
                "validation.minlength" => $"At least {parameters!["min"]} characters",
                _ => null
            };
    }

    private sealed class SilentResolver : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null) => null;
    }

    /// <summary>And an explicit rename wins over the convention.</summary>
    /// <remarks>
    ///     The half this side cannot work out: <c>[JsonPropertyName("people")]</c> is on the
    ///     property, only the generator sees it, and it arrives on the issue already decided.
    /// </remarks>
    [Fact]
    public void WriteExtensions_PrefersTheExplicitWireName()
    {
        var error = ValidationError.FromIssues([
            new ValidationIssue("validation.mincount", "Roster") { WirePath = "people" }
        ]);

        var errors = ExtensionsOf(error)["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;

        errors.Should().ContainKey("people");
        errors.Should().NotContainKey("roster", "the C# name is not what the caller sent");
    }

    /// <summary>A nested path keeps its shape: only the names change case.</summary>
    [Fact]
    public void WriteExtensions_CamelCasesEachSegmentOfANestedPath()
    {
        var error = ValidationError.FromIssues([
            ValidationIssue.For("Lines[0].ProductCode", "validation.required")
        ]);

        var errors = ExtensionsOf(error)["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;

        errors.Should().ContainKey("lines[0].productCode");
    }

    [Fact]
    public void WriteExtensions_GroupsSeveralIssuesOnTheSameProperty()
    {
        var error = ValidationError.FromIssues([
            ValidationIssue.For("Title", "validation.required"),
            ValidationIssue.For("Title", "validation.maxLength")
        ]);

        var errors = (Dictionary<string, string[]>)ExtensionsOf(error)["errors"]!;

        errors["title"].Should().BeEquivalentTo(["validation.required", "validation.maxLength"]);
    }

    /// <summary>An issue with no property path is a model-level error, the empty key in RFC 7807.</summary>
    [Fact]
    public void WriteExtensions_PutsAnIssueWithoutAPropertyPathUnderTheEmptyKey()
    {
        var error = ValidationError.FromIssues([new ValidationIssue("validation.inconsistent")]);

        ((Dictionary<string, string[]>)ExtensionsOf(error)["errors"]!).Should().ContainKey("");
    }

    /// <summary>No issues, no extension — an empty <c>errors</c> object would be noise.</summary>
    [Fact]
    public void WriteExtensions_WithoutIssues_WritesNothing()
        => ExtensionsOf(ValidationError.FromIssues([])).Should().BeEmpty();
}
