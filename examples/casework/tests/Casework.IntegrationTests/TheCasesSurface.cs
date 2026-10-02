using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Casework.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The case over HTTP: opened, read back, corrected, and refused when the rule says so.
/// </summary>
/// <remarks>
///     <para>
///         A closed set of routes, which is the point of the last test here: a case moves through
///         <c>CaseStatus</c>'s declared transitions, and no route takes a status as an input. The rule is
///         only a rule while something asserts it — otherwise an endpoint that writes the column passes
///         unnoticed, and the state machine becomes decoration.
///     </para>
///     <para>
///         Every call carries a token, and the token carries the tenant: <c>Case</c> is an
///         <c>ITenantEntity</c> and its query filter is fail-closed, so a request with no tenant resolved
///         does not see the row it just wrote. The tenant is a claim and never a header — see
///         <see cref="CaseworkTestBase.IntakeAs" />.
///     </para>
/// </remarks>
public sealed class TheCasesSurface(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    [Fact]
    public async Task ACaseIsOpened_ReadBack_AndCorrected()
    {
        var operator1 = IntakeAs(Caseworker);

        var created = await operator1.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        });

        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, $"a case is created: {body}");

        var opened = JsonDocument.Parse(body).RootElement;
        opened.TryGetProperty("id", out var identity).Should().BeTrue(
            $"the answer is the case, as [ReturnsDto<CaseDto>] says: {body}");
        var id = identity.GetGuid();

        created.Headers.Location?.OriginalString.Should().EndWith(id.ToString(),
            "201 says where the case now is, and the place it names is the case that was just opened");

        opened.GetProperty("number").GetString().Should().StartWith("CASE-",
            "the number is generated for the tenant, not supplied by the caller");
        opened.GetProperty("status").GetString().Should().Be("Open",
            "nothing has been checked yet");

        // The route the 201 points at, followed as a client would follow it.
        var read = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        read.GetProperty("subject").GetString().Should().Be("A licence for a food stall");
        // Absent, not null: the generated host serialises with WhenWritingNull, so a fact that is not
        // there is not on the wire at all. Asserting `null` here is what this test asked first, and it
        // was asserting the serializer's policy rather than the case's state.
        read.TryGetProperty("verificationAskedOn", out _).Should().BeFalse(
            "no verification has been asked for");

        var corrected = await operator1.PutAsJsonAsync($"api/cases/{id}", new
        {
            subject = "A licence for a food stall, extended",
            applicant = "A. Applicant"
        });
        corrected.IsSuccessStatusCode.Should().BeTrue(
            $"the correction is accepted: {await corrected.Content.ReadAsStringAsync()}");

        var again = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        again.GetProperty("subject").GetString().Should().Be("A licence for a food stall, extended",
            "the read reflects the correction");
        again.GetProperty("number").GetString().Should().Be(opened.GetProperty("number").GetString(),
            "correcting a case does not renumber it");
        again.GetProperty("status").GetString().Should().Be("Open",
            "and an update of what the case says does not move it");
    }

    /// <summary>
    ///     The rule on the entity refuses a subject nobody can act on, and says so in the rule's words.
    /// </summary>
    /// <remarks>
    ///     422 and not 400: it is an aggregate rule and not a malformed request — the input is
    ///     well-formed and every validation attribute on the mutation is satisfied. The message is the
    ///     one <c>[Invariant(MessageKey = "case.subject.too.short")]</c> names, resolved from the
    ///     module's translations; an assertion on the status alone would be satisfied by any refusal for
    ///     any reason.
    /// </remarks>
    [Fact]
    public async Task ASubjectNobodyCanActOnIsRefused_WithTheRulesMessage()
    {
        var operator1 = IntakeAs(Caseworker);

        var refused = await operator1.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence",
            applicant = "A. Applicant"
        });

        refused.StatusCode.Should().Be((HttpStatusCode)422, "an aggregate rule refuses what it cannot accept");

        var body = await refused.Content.ReadAsStringAsync();
        body.Should().Contain("A case needs a subject",
                "the title of the refusal, from the key the rule names")
            .And.Contain("at least ten characters",
                "and what it explains — a generic 'invariant violated' cannot say which rule refused");
    }

    /// <summary>
    ///     No route in the API <b>writes</b> a status.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not "nothing moves the status": <c>POST api/cases/{id}/verifications</c> takes a case
    ///         from <c>Open</c> to <c>InVerification</c>. The rule is that a status is never an
    ///         <b>input</b>: a caller asks the case to do something and the
    ///         state machine decides whether it can, which is the difference between a machine and a
    ///         column. The second half of this test is that assertion, and it is the half that matters.
    ///     </para>
    ///     <para>
    ///         The closed set is asserted too, and catches a route nobody meant to publish: a decision
    ///         endpoint (deciding is a message's job, not a caller's) cannot appear without this going
    ///         red. <c>POST api/organisations</c> — the one operation of this service that is not about a
    ///         case — is in the list below, and so are the two routes for the letter an organisation
    ///         receives and the template it sends in: each line is the evidence that the route is a
    ///         decision rather than a side effect.
    ///     </para>
    ///     <para>
    ///         Read over HTTP, from <c>/openapi/v1.json</c> — which is what a consumer reads, and the
    ///         only form of this assertion that also proves the document is <b>served</b>.
    ///         ⚠️ It relies on each host having its own document in its own container: a document held in
    ///         a static of the process would let whichever host loaded last answer for both hosts in this
    ///         test process — and Verify's document has no case paths at all.
    ///     </para>
    ///     <para>
    ///         ⚠️ Not from the route table either: a <c>WebApplication</c> keeps its routes in a data
    ///         source of its own and the container's <c>EndpointDataSource</c> answers an empty list,
    ///         which is a second way to measure nothing.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task NoRouteWritesAStatus()
    {
        var contract = await ReadJsonAsync(await Intake.GetAsync("/openapi/v1.json"));

        var operations = contract.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Select(verb => $"{verb.Name.ToUpperInvariant()} {path.Name}"))
            .OrderBy(operation => operation, StringComparer.Ordinal)
            .ToList();

        operations.Should().BeEquivalentTo(
        [
            "GET /api/cases/{caseId}/documents/{id}",
            "GET /api/cases/{id}",
            "GET /api/cases/{id}/letter",
            // The same letter as a Word file. Two renderers over one DocumentModel, which
            // is what the PDF endpoint's remark claims.
            "GET /api/cases/{id}/letter.docx",
            "POST /api/cases",
            "POST /api/cases/{id}/documents",
            "POST /api/cases/{id}/verifications",
            "POST /api/letter-templates",
            "POST /api/organisations",
            "PUT /api/cases/{id}",
            "PUT /api/correspondence"
        ], "what the example publishes — and still no route that writes a status");

        // And the route that exists does not become one: a status in the body of an update is not a
        // field of that operation, so it changes nothing. The control for the absence above — an
        // endpoint list can be right while the update quietly writes the column.
        var operator1 = IntakeAs(Caseworker);
        var opened = await ReadJsonAsync(await operator1.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        }));
        var id = opened.GetProperty("id").GetGuid();

        await operator1.PutAsJsonAsync($"api/cases/{id}", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant",
            status = "Approved"
        });

        var read = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        read.GetProperty("status").GetString().Should().Be("Open",
            "a case moves through the state machine's declared transitions and through nothing else");
    }
}
