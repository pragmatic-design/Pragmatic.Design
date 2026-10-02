using System.Net;
using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The published OpenAPI document, compared with the application it describes.
/// </summary>
/// <remarks>
///     <para>
///         The document is built at compile time from the same attributes the endpoints come from. That
///         makes it consistent <em>by construction</em> on names, and leaves open the question that
///         matters: does it tell the truth about what the application <b>accepts</b>?
///     </para>
///     <para>
///         The cases read the <b>served</b> document, not the generated constant: between the two sits
///         the wiring, which is the only part no generator test can cover.
///     </para>
/// </remarks>
public class ThePublishedContract(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<JsonElement> TheDocumentAsync()
        => await ReadAsync(await Client.GetAsync("/openapi/v1.json"));

    /// <summary>The document is served, and it is 3.1.</summary>
    [Fact]
    public async Task TheDocument_IsServed()
    {
        var doc = await TheDocumentAsync();

        doc.GetProperty("openapi").GetString().Should().Be("3.1.0");
        doc.GetProperty("paths").EnumerateObject().Should().NotBeEmpty(
            "a document without routes would describe an application that is not there");
    }

    /// <summary>
    ///     Types come out with their shape, not flattened to a string.
    /// </summary>
    /// <remarks>
    ///     Flattening types — together with a 3.0-style <c>nullable</c> inside a 3.1 document — is what a
    ///     generated client would inherit. Here the surface is chosen: a <c>Guid</c>, an <c>int</c>, a
    ///     nested value object and a collection three deep.
    /// </remarks>
    [Fact]
    public async Task TheTypes_KeepTheirShape()
    {
        var doc = await TheDocumentAsync();
        var schemas = doc.GetProperty("components").GetProperty("schemas");

        var id = schemas.GetProperty("AllocationTagDto").GetProperty("properties").GetProperty("id");
        id.GetProperty("type").GetString().Should().Be("string");
        id.GetProperty("format").GetString().Should().Be("uuid",
            "a Guid is a string with a format, not just a string");

        var shelf = schemas.GetProperty("StorageSlot").GetProperty("properties").GetProperty("shelf");
        shelf.GetProperty("type").GetString().Should().Be("integer");
        shelf.GetProperty("format").GetString().Should().Be("int32");

        var slot = schemas.GetProperty("AllocationDto").GetProperty("properties").GetProperty("slot");
        slot.GetProperty("$ref").GetString().Should().Be("#/components/schemas/StorageSlot",
            "the value object is a type, not a flattened field");

        doc.GetRawText().Should().NotContain("\"nullable\"",
            "`nullable` is the 3.0 form; in a 3.1 document nullability is a union type");
    }

    /// <summary>The document describes the nested shape all the way down.</summary>
    [Fact]
    public async Task TheDocument_ReachesTheThirdLevel()
    {
        var schemas = (await TheDocumentAsync()).GetProperty("components").GetProperty("schemas");

        schemas.GetProperty("OrderLineDto").GetProperty("properties")
            .GetProperty("allocations").GetProperty("items").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/AllocationDto");

        schemas.GetProperty("AllocationDto").GetProperty("properties")
            .GetProperty("tags").GetProperty("items").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/AllocationTagDto",
                "three levels of nesting arrive whole in the published contract");
    }

    /// <summary>
    ///     ⚠️ Every published schema is reachable, and every reference finds its schema.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An orphan schema is not cosmetic: whoever generates a client from the document gets a class
    ///         for every schema, so an unreachable type becomes real code on the integrator's side. And
    ///         orphans tend to be <b>entities</b>: the public contract would show internal shapes.
    ///     </para>
    ///     <para>
    ///         The manifest carries the entities on purpose — the Client SG builds the SDK from them — so
    ///         the manifest → OpenAPI step must not pour them all into the contract. The set is computed
    ///         by reachability: the operations' responses and bodies are the roots, and the closure
    ///         follows the properties — unwrapping collections and dictionaries, because a
    ///         <c>List&lt;OrderLineDto&gt;</c> property does not resolve by name.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task EverySchemaIsReachable_AndEveryReferenceResolves()
    {
        var doc = await TheDocumentAsync();
        var raw = doc.GetRawText();

        var referenced = System.Text.RegularExpressions.Regex
            .Matches(raw, @"#/components/schemas/(?<name>[A-Za-z0-9_.]+)")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var published = doc.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(p => p.Name).ToList();

        published.Where(name => !referenced.Contains(name))
            .Should().BeEmpty("a schema no route and no other schema names is a class the generated "
                + "client can neither produce nor consume");

        // The control, without which the assertion above is satisfied by publishing nothing.
        referenced.Where(name => !published.Contains(name, StringComparer.Ordinal))
            .Should().BeEmpty("a $ref that does not find its schema is a broken document, which is worse "
                + "than a noisy one");
    }

    /// <summary>
    ///     ⚠️ And the filter is by <b>reachability</b>, not by type.
    /// </summary>
    /// <remarks>
    ///     The shortcut — excluding entities — would pass the case above on every application in which
    ///     nobody answers with one. <c>GetRawOrderQuery</c> answers with <c>Order</c>, so here it would
    ///     leave a dangling <c>$ref</c>: that is why that query exists.
    /// </remarks>
    [Fact]
    public async Task AnEntityAnsweredByAnEndpoint_StaysInTheDocument()
    {
        var doc = await TheDocumentAsync();

        doc.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(p => p.Name)
            .Should().Contain("Order",
                "an endpoint answers with the entity, so the document must describe it");
    }

    /// <summary>
    ///     ⚠️ The question that matters: is what the document declares required really required?
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The document is a claim towards the integrator. If it declares `required` a property the
    ///         application accepts not receiving, the reader writes more code than needed; if it is silent
    ///         on one that is needed, they find out in production. Neither can be checked by reading the
    ///         document alone — the request has to be sent.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task WhatTheDocumentCallsRequired_IsRequiredOfTheCaller()
    {
        var schemas = (await TheDocumentAsync()).GetProperty("components").GetProperty("schemas");

        var required = schemas.GetProperty("CreateOrderMutationRequest")
            .GetProperty("required").EnumerateArray()
            .Select(x => x.GetString()).ToList();

        required.Should().Contain("lines", "it is what the document promises");

        // The same create, without the property the document declares required.
        var response = await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
        });

        // Not «other than 201»: that would pass with a 500 too, and a server fault is not a refusal. The
        // document's promise is kept only if the refusal is the CALLER's.
        ((int)response.StatusCode).Should().BeInRange(400, 499,
            "the document declares it required, so omitting it is the caller's error — "
            + "and accepting it would contradict the document in front of the integrator");
    }

    /// <summary>
    ///     ⚠️ The constraints the server applies are in the document, with the right keyword.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>CheckEveryRuleAction</c> carries one property per rule, and <c>EveryRuleIsExecuted</c>
    ///         shows that each one refuses. Here the question is the other half: does whoever reads the
    ///         contract know them before calling? Minimum length, range, regular expression, number of
    ///         elements — each applied constraint must be stated, not only <c>maxLength</c>.
    ///     </para>
    ///     <para>
    ///         ⚠️ The control case is the collection: <c>[MaxLength(2)]</c> on a list counts elements,
    ///         and in the document it must be <c>maxItems</c> — <c>maxLength</c> on an array is a keyword
    ///         no reader applies.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheConstraintsTheServerApplies_AreInTheDocument()
    {
        var properties = (await TheDocumentAsync()).GetProperty("components").GetProperty("schemas")
            .GetProperty("CheckEveryRuleActionRequest").GetProperty("properties");

        properties.GetProperty("atLeastThree").GetProperty("minLength").GetInt32().Should().Be(3);
        properties.GetProperty("atMostFive").GetProperty("maxLength").GetInt32().Should().Be(5);
        properties.GetProperty("betweenTwoAndFour").GetProperty("minLength").GetInt32().Should().Be(2);
        properties.GetProperty("betweenTwoAndFour").GetProperty("maxLength").GetInt32().Should().Be(4);
        properties.GetProperty("threeCapitals").GetProperty("pattern").GetString().Should().Be("^[A-Z]{3}$");
        properties.GetProperty("email").GetProperty("format").GetString().Should().Be("email");
        properties.GetProperty("url").GetProperty("format").GetString().Should().Be("uri");
        properties.GetProperty("inRange").GetProperty("minimum").GetDouble().Should().Be(1);
        properties.GetProperty("inRange").GetProperty("maximum").GetDouble().Should().Be(10);
        properties.GetProperty("overTen").GetProperty("exclusiveMinimum").GetDouble().Should().Be(10);
        properties.GetProperty("tenOrUnder").GetProperty("maximum").GetDouble().Should().Be(10);
        properties.GetProperty("atLeastOne").GetProperty("minItems").GetInt32().Should().Be(1);
        properties.GetProperty("oneToThree").GetProperty("maxItems").GetInt32().Should().Be(3);

        var notTooLong = properties.GetProperty("notTooLong");
        notTooLong.GetProperty("maxItems").GetInt32().Should().Be(2,
            "[MaxLength] on a collection counts the elements");
        notTooLong.TryGetProperty("maxLength", out _).Should().BeFalse(
            "maxLength on an array is a keyword no reader applies");
    }
}
