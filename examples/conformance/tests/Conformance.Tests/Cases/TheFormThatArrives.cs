using System.Net;
using System.Net.Http.Headers;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A <c>[FromForm]</c> written like any other operation — <c>required … init</c>, a typed field, a rule —
///     compiles and <b>executes</b>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A <c>{ get; set; } = null!</c> upload with only string fields survives by accident the three
///         ways the generator can get this shape wrong: fields assigned after construction, every field
///         bound as a string, a validator for a <c>…Body</c> the form does not have. This case exists so
///         that the generator is <em>executed</em> on the harder shape, not only compiled.
///     </para>
///     <para>
///         The receipt reports the bytes <b>read from the stream</b>, not the declared length: it is the
///         difference between a file accepted and a file that arrived.
///     </para>
/// </remarks>
public class TheFormThatArrives(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];

    private static MultipartFormDataContent AForm(string? label = "invoice", string? folderId = null)
    {
        var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(Payload);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // The field name is the [FromForm] property name.
        form.Add(file, "File", "receipt.bin");

        if (label is not null)
            form.Add(new StringContent(label), "Label");
        form.Add(new StringContent(folderId ?? Guid.NewGuid().ToString()), "FolderId");

        return form;
    }

    [Fact]
    public async Task ARequiredFile_ARequiredField_AndATypedField_Arrive()
    {
        var orderId = Guid.NewGuid();
        var folder = Guid.NewGuid();

        var response = await Client.PostAsync($"/api/orders/{orderId}/attachments", AForm(folderId: folder.ToString()));

        var receipt = await ReadAsync(response);
        Assert.Equal(Payload.Length, receipt.GetProperty("bytes").GetInt64());
        Assert.Equal("receipt.bin", receipt.GetProperty("fileName").GetString());
        Assert.Equal("invoice", receipt.GetProperty("label").GetString());
        Assert.Equal(folder, receipt.GetProperty("folderId").GetGuid());
        Assert.Equal(orderId, receipt.GetProperty("orderId").GetGuid());
    }

    /// <summary>The control: a missing required field is refused, not filled.</summary>
    [Fact]
    public async Task AMissingRequiredField_IsRefused()
    {
        var response = await Client.PostAsync($"/api/orders/{Guid.NewGuid()}/attachments", AForm(label: null));
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 400 — Label is required and missing — received {(int)response.StatusCode} {text}");
        Assert.Contains("Label", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The second control: the typed field is bound as its type, so a malformed value is 400.</summary>
    /// <remarks>
    ///     ⚠️ Bound as a string it would not compile; and if it compiled by passing the raw string, a
    ///     malformed <c>Guid</c> would reach the operation as <c>default</c>.
    /// </remarks>
    [Fact]
    public async Task AMalformedTypedField_IsRefused()
    {
        var response = await Client.PostAsync($"/api/orders/{Guid.NewGuid()}/attachments", AForm(folderId: "not-a-guid"));
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 400 — FolderId is not a Guid — received {(int)response.StatusCode} {text}");
    }

    /// <summary>The third control: the rule on the form field runs on the operation, not on a body that does not exist.</summary>
    [Fact]
    public async Task TheRuleOnAFormField_StillRuns()
    {
        var response = await Client.PostAsync($"/api/orders/{Guid.NewGuid()}/attachments", AForm(label: ""));
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422 — [MinLength(1)] on Label — received {(int)response.StatusCode} {text}");
    }
}
