using Pragmatic.Endpoints.Binding;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Binding;

/// <summary>
///     The binder generated endpoints use instead of ASP.NET's, which is reflection-based and
///     therefore unusable under Native AOT.
/// </summary>
/// <remarks>
///     Every overload is picked at compile time by the generator from the parameter's declared type,
///     so these tests are about the parsing contract: what counts as absent, what counts as malformed,
///     and which culture decides.
/// </remarks>
public class RequestBinderTests
{
    private enum Shade { Plain = 0, Bold = 1 }

    [Fact]
    public void ARequiredValue_BindsWhenWellFormed()
    {
        RequestBinder.TryBind<int>("42", out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    public void ARequiredValue_FailsWhenAbsentOrMalformed(string? raw)
    {
        RequestBinder.TryBind<int>(raw, out _).Should().BeFalse("the caller answers 400 rather than guessing");
    }

    [Fact]
    public void Decimals_ParseInvariantly()
    {
        // A route value is part of a URL, not of a locale. On an it-IT machine a culture-sensitive
        // parse would read "1.5" as 15, and the same request would mean two different things on two
        // servers.
        RequestBinder.TryBind<decimal>("1.5", out var value).Should().BeTrue();
        value.Should().Be(1.5m);
    }

    [Fact]
    public void AnOptionalValue_FallsBackWhenAbsent()
    {
        RequestBinder.TryBindOptional<int>(null, fallback: 20, out var value).Should().BeTrue();
        value.Should().Be(20);
    }

    [Fact]
    public void AnOptionalValue_StillFailsWhenMalformed()
    {
        // Absent and wrong are different: omitting a page size means "use the default", sending
        // "abc" means the caller got it wrong and should be told.
        RequestBinder.TryBindOptional<int>("abc", fallback: 20, out _).Should().BeFalse();
    }

    [Fact]
    public void AnEnum_BindsByNameCaseInsensitively()
    {
        RequestBinder.TryBindEnum<Shade>("bold", out var value).Should().BeTrue();
        value.Should().Be(Shade.Bold);
    }

    [Fact]
    public void AnEnum_BindsByNumericValue()
    {
        RequestBinder.TryBindEnum<Shade>("1", out var value).Should().BeTrue();
        value.Should().Be(Shade.Bold);
    }

    [Fact]
    public void AnUnknownEnumName_Fails()
    {
        RequestBinder.TryBindEnum<Shade>("chartreuse", out _).Should().BeFalse();
    }

    [Fact]
    public void AnOptionalEnum_FallsBackWhenAbsent()
    {
        RequestBinder.TryBindEnumOptional<Shade>("", fallback: Shade.Bold, out var value).Should().BeTrue();
        value.Should().Be(Shade.Bold);
    }

    [Fact]
    public void AnEmptyString_IsPresent()
    {
        // `?note=` is a caller saying "empty", which is not the same as not asking at all.
        RequestBinder.TryBindString("", out var value).Should().BeTrue();
        value.Should().BeEmpty();
    }

    [Fact]
    public void AMissingString_IsAbsent()
    {
        RequestBinder.TryBindString(null, out _).Should().BeFalse();
    }

    [Fact]
    public void ManyValues_BindTogether()
    {
        RequestBinder.TryBindMany<int>(["1", "2", "3"], out var values).Should().BeTrue();
        values.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void OneMalformedValue_FailsTheWholeList()
    {
        // Half-binding a list would hand the action a shorter list than the caller sent, which is a
        // wrong answer rather than an error.
        RequestBinder.TryBindMany<int>(["1", "oops", "3"], out var values).Should().BeFalse();
        values.Should().BeEmpty();
    }
}
