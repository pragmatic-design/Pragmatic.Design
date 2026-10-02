using System.Net;
using System.Text.Json;

namespace Pragmatic.Testing;

/// <summary>
///     Typed HTTP contract assertions for Pragmatic E2E tests — the vocabulary the generated contract tests use
///     (#7). Each returns the response so calls chain, and throws a <see cref="PragmaticTestAssertionException"/>
///     naming the expected vs actual status when it does not match.
/// </summary>
public static class PragmaticHttpAssertions
{
    /// <summary>Asserts the response has the expected status code.</summary>
    public static HttpResponseMessage ShouldHaveStatus(this HttpResponseMessage response, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode != expected)
            throw new PragmaticTestAssertionException(
                $"Expected HTTP {(int)expected} {expected} but the request to '{response.RequestMessage?.RequestUri}' " +
                $"returned {(int)response.StatusCode} {response.StatusCode}.");
        return response;
    }

    /// <summary>200 OK.</summary>
    public static HttpResponseMessage ShouldBeOk(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.OK);

    /// <summary>201 Created.</summary>
    public static HttpResponseMessage ShouldBeCreated(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.Created);

    /// <summary>204 No Content.</summary>
    public static HttpResponseMessage ShouldBeNoContent(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.NoContent);

    /// <summary>400 Bad Request — the request could not be read: malformed body, wrong type.</summary>
    /// <remarks>
    ///     ⚠️ Not for a validation failure. A validator that refuses an understood request answers
    ///     422 — see <see cref="ShouldBeUnprocessable" />.
    /// </remarks>
    public static HttpResponseMessage ShouldBeBadRequest(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.BadRequest);

    /// <summary>422 Unprocessable Entity — the request was understood and the rules refuse it.</summary>
    /// <remarks>
    ///     What a failed validator answers. The distinction matters to the caller: 400 means the client
    ///     built the request wrong and has a bug, 422 means it built it correctly and the person needs
    ///     to be told something.
    /// </remarks>
    public static HttpResponseMessage ShouldBeUnprocessable(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);

    /// <summary>401 Unauthorized (no/invalid credentials).</summary>
    public static HttpResponseMessage ShouldBeUnauthorized(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.Unauthorized);

    /// <summary>403 Forbidden (authenticated but lacking the permission).</summary>
    public static HttpResponseMessage ShouldBeForbidden(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.Forbidden);

    /// <summary>404 Not Found.</summary>
    public static HttpResponseMessage ShouldBeNotFound(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.NotFound);

    /// <summary>
    ///     Asserts the request was <b>rejected</b> — a 4xx, not a 2xx success. Used for the "without the required
    ///     permission" contract: the security-relevant invariant is that an unprivileged caller does not succeed.
    ///     A clean 403 is ideal, but Pragmatic's pipeline may reject earlier (400 on body binding, 404 on the
    ///     entity lookup) before the authorization filter runs; any 4xx still means the caller did not get through.
    ///     A 2xx (the caller succeeded) or 5xx (a server error, not a rejection) fails the assertion.
    ///     <para>
    ///         ⚠️ Also what a generated create-contract test asserts for a body missing its required
    ///         fields, and for the same reason: which of 400 and 422 comes back depends on whether the
    ///         missing value stops the object being constructed at all, so two creates, both with a
    ///         required field, can answer differently to the same empty body — pinning either status
    ///         would make some generated tests assert a contract the API does not have.
    ///     </para>
    /// </summary>
    public static HttpResponseMessage ShouldBeRejected(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if ((int)response.StatusCode is < 400 or >= 500)
            throw new PragmaticTestAssertionException(
                $"Expected the request to '{response.RequestMessage?.RequestUri}' to be rejected (4xx) for an " +
                $"unprivileged caller, but it returned {(int)response.StatusCode} {response.StatusCode}.");
        return response;
    }

    /// <summary>409 Conflict (e.g. an illegal state transition).</summary>
    public static HttpResponseMessage ShouldBeConflict(this HttpResponseMessage response) =>
        response.ShouldHaveStatus(HttpStatusCode.Conflict);

    /// <summary>
    ///     Asserts the request was <b>authorized</b> — not 401/403. Use for the "with the required permission"
    ///     contract: the caller reaches the endpoint (the result may still be 404/400 depending on data), the
    ///     point being that authorization did not block it.
    /// </summary>
    /// <remarks>
    ///     &#9888; On its own this is satisfied by "never got there". A request refused before routing — a
    ///     middleware answering 400 to every call — is not forbidden either, and a whole generated
    ///     contract suite can report success on exactly that. Prefer
    ///     <see cref="ShouldBeAuthorizedUnlike" /> wherever the unprivileged call is available to compare
    ///     against; this one remains for the cases where there is no pair.
    /// </remarks>
    public static HttpResponseMessage ShouldNotBeForbidden(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new PragmaticTestAssertionException(
                $"Expected the request to '{response.RequestMessage?.RequestUri}' to be authorized, " +
                $"but it returned {(int)response.StatusCode} {response.StatusCode}.");
        return response;
    }

    /// <summary>
    ///     Asserts the privileged caller was authorized <b>and</b> that authorization made a difference:
    ///     the same request without the permission must not have been answered the same way.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         &#9888; The pair is the measurement, and "not forbidden" alone is not. When something
    ///         refuses every request before it reaches a route — a tenant middleware answering 400, a
    ///         missing header — the privileged call is not forbidden either, and the test passes having
    ///         proved nothing. That is how fifty-four generated contract tests reported success while
    ///         every request was refused: the unprivileged one returned 400, which satisfies
    ///         <see cref="ShouldBeRejected" />, and the privileged one returned 400, which satisfies
    ///         <see cref="ShouldNotBeForbidden" />.
    ///     </para>
    ///     <para>
    ///         Two identical statuses mean the endpoint answered both callers the same way, so the
    ///         permission changed nothing observable. The legitimate outcomes all differ: 403 against
    ///         404 for a read of a random id, 403 against 400 for a create with a synthesised body, 403
    ///         against 2xx for a create the shape can carry.
    ///     </para>
    /// </remarks>
    /// <param name="authorized">The response to the caller carrying the permission.</param>
    /// <param name="unprivileged">The response to the same request without it.</param>
    public static HttpResponseMessage ShouldBeAuthorizedUnlike(
        this HttpResponseMessage authorized,
        HttpResponseMessage unprivileged)
    {
        ArgumentNullException.ThrowIfNull(authorized);
        ArgumentNullException.ThrowIfNull(unprivileged);

        authorized.ShouldNotBeForbidden();

        if (authorized.StatusCode == unprivileged.StatusCode)
            throw new PragmaticTestAssertionException(
                $"The request to '{authorized.RequestMessage?.RequestUri}' returned " +
                $"{(int)authorized.StatusCode} {authorized.StatusCode} both with the required permission and " +
                "without it, so the permission changed nothing observable. Something is refusing the request " +
                "before authorization runs - a middleware, a missing header, an unrouted path - and " +
                "'not forbidden' is satisfied by never getting there.");

        return authorized;
    }

    /// <summary>Asserts a 2xx success status (any).</summary>
    public static HttpResponseMessage ShouldBeSuccess(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.IsSuccessStatusCode)
            throw new PragmaticTestAssertionException(
                $"Expected a 2xx success status but the request to '{response.RequestMessage?.RequestUri}' " +
                $"returned {(int)response.StatusCode} {response.StatusCode}." + Explanation(response));
        return response;
    }

    /// <summary>
    ///     Asserts the create said what it created, and returns that identifier.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <c>Location</c> header first, since that is the address the server itself gave. Then the
    ///         body: a bare id, or an object carrying one. ⚠️ A Pragmatic app answers a create with
    ///         <c>Results.Created((string?)null, value)</c> — 201, the id in the body, no header — so a
    ///         contract that could only read the header could never follow its own create.
    ///     </para>
    ///     <para>
    ///         ⚠️ And it fails rather than returning: a contract that creates something in order to act on
    ///         it, and gives up quietly when it cannot find it, is a test that asserts nothing and reports
    ///         success.
    ///     </para>
    /// </remarks>
    public static async Task<string> ShouldIdentifyTheCreatedAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var location = response.Headers.Location?.ToString();
        if (!string.IsNullOrEmpty(location))
        {
            var trimmed = location!.TrimEnd('/');
            return trimmed.Substring(trimmed.LastIndexOf('/') + 1);
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (IdentifierIn(body) is { } id)
            return id;

        throw new PragmaticTestAssertionException(
            $"The request to '{response.RequestMessage?.RequestUri}' returned " +
            $"{(int)response.StatusCode} {response.StatusCode} without saying what it created: no Location " +
            "header, and no identifier in the body. A contract that has to act on the created entity " +
            "cannot address it." + Explanation(response));
    }

    /// <summary>The identifier a create body carries: the value itself, or its <c>id</c> member.</summary>
    private static string? IdentifierIn(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return root.ToString();

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    return property.Value.ToString();
            }

            return null;
        }
        catch (JsonException)
        {
            // A body that is not JSON tells us nothing about an identifier; the caller reports that.
            return null;
        }
    }

    /// <summary>
    ///     The server's own account of the refusal, when it gave one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A generated contract is the test whose author is not in the room: nobody will add a
    ///     breakpoint to find out which field a validator rejected. The status code alone sends the reader
    ///     to the wrong place — usually to the generator — while the answer is in the body.
    ///     <para>
    ///         Read synchronously, and deliberately: this runs only on the failure path, where the
    ///         alternative is an assertion that cannot explain itself. Truncated, because a problem-details
    ///         payload with a stack trace is not more informative for being longer.
    ///     </para>
    /// </remarks>
    private static string Explanation(HttpResponseMessage response)
    {
        string body;
        try
        {
            body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // The body is a diagnostic aid; failing to read it must not replace the assertion's own
            // failure with an unrelated one.
            return $" The response body could not be read: {ex.GetType().Name}.";
        }

        if (string.IsNullOrWhiteSpace(body))
            return "";

        body = body.Trim();
        if (body.Length > BodyExcerptLength)
            body = body.Substring(0, BodyExcerptLength) + "…";

        return $" The server said: {body}";
    }

    private const int BodyExcerptLength = 800;
}
