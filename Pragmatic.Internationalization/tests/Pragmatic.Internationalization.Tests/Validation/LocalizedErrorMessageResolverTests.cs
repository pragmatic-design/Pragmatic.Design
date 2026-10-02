using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.AspNetCore.Result;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Result;

namespace Pragmatic.Internationalization.Tests.Validation;

[Collection("I18nContext")]
public class LocalizedErrorMessageResolverTests
{
    public LocalizedErrorMessageResolverTests()
    {
        I18NContext.Clear();
        I18NContext.SetCulture("en");
    }

    [Fact]
    public void Resolve_KeyExistsForError_ReturnsLocalizedMessage()
    {
        var resolver = CreateResolver(("en", "error.not.found.detail", "Resource was not found"));
        var error = new TestError("NOT_FOUND");

        var result = resolver.Resolve(error.Code, error);

        result.Should().Be("Resource was not found");
    }

    /// <summary>
    ///     The bare key is not read. It would make a translation file impossible to hand to the source
    ///     generator: a key beside its own '.title' asks the generated class for a member and a nested
    ///     class of the same name.
    /// </summary>
    [Fact]
    public void Resolve_BareKeyWithoutDetailSuffix_ReturnsNull()
    {
        var resolver = CreateResolver(("en", "error.not.found", "Resource was not found"));
        var error = new TestError("NOT_FOUND");

        resolver.Resolve(error.Code, error).Should().BeNull();
    }

    [Fact]
    public void Resolve_KeyMissing_ReturnsNull()
    {
        var resolver = CreateResolver();
        var error = new TestError("NOT_FOUND");

        var result = resolver.Resolve(error.Code, error);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithParameters_InterpolatesNamedTokens()
    {
        var resolver = CreateResolver(("en", "error.too.many.nights.detail", "Cannot exceed {limit} nights"));
        var error = new TestError("TOO_MANY_NIGHTS")
        {
            Parameters = new Dictionary<string, object> { ["limit"] = 14 }
        };

        var result = resolver.Resolve(error.Code, error);

        result.Should().Be("Cannot exceed 14 nights");
    }

    [Fact]
    public void Resolve_ParameterMissing_LeavesPlaceholderIntact()
    {
        var resolver = CreateResolver(("en", "error.bad.value.detail", "Value {missing} here"));
        var error = new TestError("BAD_VALUE")
        {
            Parameters = new Dictionary<string, object> { ["other"] = 1 }
        };

        var result = resolver.Resolve(error.Code, error);

        result.Should().Be("Value {missing} here");
    }

    [Fact]
    public void Resolve_NoErrorContext_DerivesKeyFromCode()
    {
        var resolver = CreateResolver(("en", "error.some.code.detail", "Plain message"));

        var result = resolver.Resolve("SOME_CODE");

        result.Should().Be("Plain message");
    }

    [Fact]
    public void ResolveTitle_KeyExists_ReturnsTitleVariant()
    {
        var resolver = CreateResolver(("en", "error.not.found.title", "Not Found"));
        var error = new TestError("NOT_FOUND");

        var result = resolver.ResolveTitle(error.Code, error);

        result.Should().Be("Not Found");
    }

    [Fact]
    public void ResolveTitle_KeyMissing_ReturnsNull()
    {
        var resolver = CreateResolver();
        var error = new TestError("NOT_FOUND");

        resolver.ResolveTitle(error.Code, error).Should().BeNull();
    }

    /// <summary>
    ///     An issue's key, read as it is, in the current culture, with the issue's
    ///     parameters: a validation message has no title to tell apart from, so no suffix.
    /// </summary>
    [Fact]
    public void ResolveKey_ReadsTheKeyInTheCurrentCulture_WithTheIssuesParameters()
    {
        IErrorMessageResolver resolver = CreateResolver(
            ("en", "validation.minlength", "At least {min} characters"),
            ("it", "validation.minlength", "Almeno {min} caratteri"));
        I18NContext.SetCulture("it");

        resolver.ResolveKey("validation.minlength", new Dictionary<string, object> { ["min"] = 3 })
            .Should().Be("Almeno 3 caratteri");
    }

    [Fact]
    public void ResolveKey_UnknownKey_ReturnsNull()
    {
        IErrorMessageResolver resolver = CreateResolver(("en", "validation.minlength", "At least {min} characters"));

        resolver.ResolveKey("validation.unheard_of").Should().BeNull();
    }

    private static LocalizedErrorMessageResolver CreateResolver(params (string Culture, string Key, string Value)[] entries)
    {
        var inner = new InMemoryLocalizationProvider();
        foreach (var (culture, key, value) in entries)
            inner.AddString(culture, key, value);

        var composite = new CompositeLocalizationProvider([inner]);
        return new LocalizedErrorMessageResolver(composite);
    }

    private sealed record TestError(string ErrorCode) : Error
    {
        public override string Code => ErrorCode;
        public override int StatusCode => 400;
    }
}
