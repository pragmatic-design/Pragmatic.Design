using System.Text.Json;
using Pragmatic.SourceGenerator.Features.Glossary;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     The generator targets netstandard2.0 and cannot call <see cref="JsonNamingPolicy" />, so it
///     reimplements the camelCase conversion. The reimplementation is only worth anything if it agrees
///     with the policy the runtime actually applies — these tests compare the two directly, including the
///     acronym cases where a naive "lowercase the first char" diverges.
/// </summary>
public class JsonSchemaNamingTests
{
    [Theory]
    [InlineData("OrderId")]
    [InlineData("Total")]
    [InlineData("OccurredAt")]
    [InlineData("ID")]
    [InlineData("IOStream")]
    [InlineData("URLValue")]
    [InlineData("XMLHttpRequest")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("alreadyCamel")]
    [InlineData("_underscore")]
    [InlineData("")]
    public void ToCamelCase_MatchesJsonNamingPolicyCamelCase(string name)
        => JsonSchemaNaming.ToCamelCase(name).Should().Be(JsonNamingPolicy.CamelCase.ConvertName(name));
}
