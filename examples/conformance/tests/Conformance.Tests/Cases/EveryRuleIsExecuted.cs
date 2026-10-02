using System.Net;
using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     Every validation rule is <b>executed</b>, and names the property it refuses.
/// </summary>
/// <remarks>
///     <para>
///         That the generator renders them all is verified by reading its code: thirty-three branches plus
///         <c>[Required]</c> and <c>[ValidateElements]</c>. The question no snapshot can ask is the other
///         one — <b>are they invoked?</b> A generated rule nobody calls is, in production,
///         indistinguishable from valid data.
///     </para>
///     <para>
///         ⚠️ The control is inside the case, not beside it. Each row breaks <b>a single</b> property of a
///         body the first test proves valid: if the request is refused, the only difference from the
///         accepted one is the field under test. Without this structure, «422» could be explained by any
///         other rule violated by accident.
///     </para>
///     <para>
///         And the assertion is on the <b>rule's key</b> under the property's name, not on the status code
///         alone: a 422 says something was refused, not that it was refused by what we meant to measure.
///         ⚠️ Asserting the name alone, the <c>[NotEmpty]</c> case on a <c>required</c> property would pass
///         because the presence guard — <c>validation.required</c> — refused, with the rule's branch
///         unreachable. The key is what tells the two apart.
///     </para>
/// </remarks>
public class EveryRuleIsExecuted(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>The body that respects every rule, and the base of every negative case.</summary>
    private static Dictionary<string, object?> AValidBody() => new()
    {
        ["present"] = "x",
        ["notEmpty"] = "x",
        ["notBlank"] = "x",
        ["atLeastThree"] = "abc",
        ["atMostFive"] = "abcde",
        ["betweenTwoAndFour"] = "abc",
        ["email"] = "a@b.com",
        ["phone"] = "+390212345678",
        ["url"] = "https://example.com",
        ["threeCapitals"] = "ABC",
        ["card"] = "4111111111111111",
        ["guidText"] = "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        ["inRange"] = 5,
        ["positive"] = 1,
        ["negative"] = -1,
        ["overTen"] = 11,
        ["tenOrOver"] = 10,
        ["underTen"] = 9,
        ["tenOrUnder"] = 10,
        ["atLeastOne"] = new[] { "a" },
        ["longEnough"] = new[] { "a", "b" },
        ["notTooLong"] = new[] { "a" },
        ["withinBounds"] = new[] { "a" },
        ["atMostTwo"] = new[] { "a" },
        ["filled"] = new[] { "a" },
        ["oneToThree"] = new[] { "a" },
        ["mustEqual"] = "same",
        ["mustDiffer"] = "other",
        ["mirror"] = "same",
        ["fromTheList"] = "red",
        ["aboveFloor"] = 10,
        ["belowCeiling"] = 1,
        // At the boundary, equal to the reference: it is what tells them from the two strict ones.
        ["floorOrAbove"] = 1,
        ["ceilingOrBelow"] = 10,
        ["floor"] = 1,
        ["ceiling"] = 10,
        ["neededWhenTriggered"] = "here",
        ["neededWhenNotTriggered"] = null,
        ["trigger"] = true,
        ["later"] = DateTime.UtcNow.AddDays(1),
        ["earlier"] = DateTime.UtcNow.AddDays(-1),
        ["tint"] = 1,
        ["people"] = new[] { "ada" },
    };

    /// <summary>The case that makes all the others a measure: as it is, the body passes.</summary>
    [Fact]
    public async Task TheValidBody_IsAccepted()
    {
        var response = await PostAsync("/api/rules", AValidBody());
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"every negative case breaks a single property of this body; if it does not pass, none of "
            + $"them measures the rule it declares. Response: {(int)response.StatusCode} {body}");
    }

    [Theory]
    [InlineData("present", "", "required")]
    [InlineData("notEmpty", "", "notempty")]
    [InlineData("notBlank", "   ", "notwhitespace")]
    [InlineData("atLeastThree", "ab", "minlength")]
    [InlineData("atMostFive", "abcdef", "maxlength")]
    [InlineData("betweenTwoAndFour", "abcde", "length")]
    [InlineData("email", "not-an-email", "email")]
    [InlineData("phone", "not-a-phone", "phone")]
    [InlineData("url", "not a url", "url")]
    [InlineData("threeCapitals", "abc", "regex")]
    [InlineData("card", "1234", "creditcard")]
    [InlineData("guidText", "not-a-guid", "guid")]
    [InlineData("inRange", 99, "range")]
    [InlineData("positive", -1, "positive")]
    [InlineData("negative", 1, "negative")]
    [InlineData("overTen", 10, "greaterthan")]
    [InlineData("tenOrOver", 9, "greaterthanorequal")]
    [InlineData("underTen", 10, "lessthan")]
    [InlineData("tenOrUnder", 11, "lessthanorequal")]
    [InlineData("fromTheList", "blue", "oneof")]
    [InlineData("mustDiffer", "same", "notequalto")]
    public async Task AViolatedRule_IsRefusedAndNamesItsProperty(
        string property, object badValue, string rule)
    {
        var body = AValidBody();
        body[property] = badValue;

        var response = await PostAsync("/api/rules", body);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"the rule on '{property}' must be executed, not only generated. Response: "
            + $"{(int)response.StatusCode} {text}");

        ShouldName(text, property, rule);
    }

    /// <summary>The rules that compare two properties, and those on collections.</summary>
    /// <remarks>
    ///     Separate because the value to break is not scalar: a collection and a cross-property comparison
    ///     do not fit an <c>[InlineData]</c> without becoming unreadable.
    /// </remarks>
    [Fact]
    public async Task TheRulesThatCompareOrCount_AreExecutedToo()
    {
        await RefusedAsync("atLeastOne", Array.Empty<string>(), "mincount");
        // ⚠️ [MinLength] on a collection: the attribute declares «a string or a collection», so it must
        // count the elements — `.Length` on a List would be CS1061 inside a generated file. This case is
        // the proof that the attribute's promise is kept.
        await RefusedAsync("longEnough", new[] { "a" }, "minlength");
        // And the other two length rules, on the same promise: they count elements, not characters.
        await RefusedAsync("notTooLong", new[] { "a", "b", "c" }, "maxlength");
        await RefusedAsync("withinBounds", Array.Empty<string>(), "length");
        await RefusedAsync("atMostTwo", new[] { "a", "b", "c" }, "maxcount");
        await RefusedAsync("filled", Array.Empty<string>(), "notempty");
        await RefusedAsync("oneToThree", new[] { "a", "b", "c", "d" }, "count");
        await RefusedAsync("mustEqual", "different", "equalto");
        await RefusedAsync("aboveFloor", 0, "greaterthanproperty");
        await RefusedAsync("belowCeiling", 99, "lessthanproperty");
        await RefusedAsync("floorOrAbove", 0, "greaterthanorequalproperty");
        await RefusedAsync("ceilingOrBelow", 11, "lessthanorequalproperty");
        await RefusedAsync("later", DateTime.UtcNow.AddDays(-1), "future_date");
        await RefusedAsync("earlier", DateTime.UtcNow.AddDays(1), "past_date");
        await RefusedAsync("tint", 99, "enum");
    }

    /// <summary>The two conditional rules, one for each side of the same condition.</summary>
    /// <remarks>
    ///     ⚠️ For <c>[RequiredIfNot]</c> the broken property is not the refused one: <c>trigger</c> is
    ///     lowered, and the missing one becomes <c>neededWhenNotTriggered</c>, which in the valid body is
    ///     already <c>null</c>. Without this case, the «not» half of the pair would be generated and never
    ///     executed by any test.
    /// </remarks>
    [Fact]
    public async Task TheConditionalRules_AreExecutedOnBothSidesOfTheCondition()
    {
        await RefusedAsync("neededWhenTriggered", null, "requiredif");
        await RefusedAsync("trigger", false, "requiredifnot", refusedProperty: "neededWhenNotTriggered");
    }

    /// <summary>
    ///     ⚠️ The error names the property as the caller sent it, not as it is declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Roster</c> travels as <c>people</c> because of <c>[JsonPropertyName]</c>. The comparison
    ///         here is <b>exact</b>, and that is the point: the integrator holds the published document,
    ///         looks for <c>people</c> in the errors map, and must not find <c>Roster</c> instead — the
    ///         field to highlight would stay unflagged, and the user would see «something is wrong» next
    ///         to nothing.
    ///     </para>
    ///     <para>
    ///         The two halves are each declared where they are known: the explicit rename travels on the
    ///         issue because only the generator sees it, the camelCase convention is applied by whoever
    ///         writes the response because only they know it.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheError_NamesThePropertyAsTheCallerSentIt()
    {
        var body = AValidBody();
        body["people"] = Array.Empty<string>();

        var response = await PostAsync("/api/rules", body);
        var text = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(422, $"the rule must refuse. Response: {text}");

        text.Should().Contain("\"people\"",
            "the name in the error is the one the caller sent and the contract publishes");
        text.Should().NotContain("Roster",
            "the C# name does not belong on the wire: only whoever reads the source knows it");
    }

    /// <summary>And where nobody renames, the convention applies: camelCase, not PascalCase.</summary>
    [Fact]
    public async Task TheError_UsesTheWireConventionForTheRest()
    {
        var body = AValidBody();
        body["notEmpty"] = "";

        var text = await (await PostAsync("/api/rules", body)).Content.ReadAsStringAsync();

        text.Should().Contain("\"notEmpty\"",
            "the wire convention is camelCase, and it is the one the document publishes");
        text.Should().NotContain("\"NotEmpty\"",
            "the C# name stays on PropertyPath, for whoever reads the errors in-process");
    }

    private async Task RefusedAsync(string property, object? badValue, string rule, string? refusedProperty = null)
    {
        var body = AValidBody();
        body[property] = badValue;

        var response = await PostAsync("/api/rules", body);
        var text = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(422,
            $"the rule '{rule}' on '{property}' must be executed. Response: "
            + $"{(int)response.StatusCode} {text}");

        ShouldName(text, refusedProperty ?? property, rule);
    }

    /// <summary>
    ///     The <c>errors</c> map must hold, under the property, the expected rule's key.
    /// </summary>
    /// <remarks>
    ///     A comparison on the key and not on the name alone: two different rules on the same property
    ///     give the same 422 and the same name, and only the key says which of the two spoke.
    /// </remarks>
    private static void ShouldName(string text, string property, string rule)
    {
        var errors = JsonDocument.Parse(text).RootElement.GetProperty("errors");

        errors.TryGetProperty(property, out var issues).Should().BeTrue(
            $"the error must name '{property}', the refused property. Response: {text}");

        issues.EnumerateArray().Select(i => i.GetString()).Should().Contain($"validation.{rule}",
            $"the rule refusing '{property}' must be '{rule}', not another one on the same "
            + $"property. Response: {text}");
    }
}
