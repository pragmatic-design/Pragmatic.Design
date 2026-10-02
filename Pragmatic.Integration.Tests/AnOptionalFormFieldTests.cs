using System.Net;
using System.Net.Http.Json;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     A nullable form field may be left out of the form; a non-nullable one may not.
/// </summary>
/// <remarks>
///     ⚠️ A nullable reference field was bound as required, so a form without it answered 400 — the
///     Showcase's photo upload refused a photo without a caption.
/// </remarks>
public sealed class AnOptionalFormFieldTests
{
    private static async Task<HttpResponseMessage> PostAsync(params (string Key, string Value)[] fields)
    {
        await using var factory = new AuthenticatingTestFactory(endpointOptions: null);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/forms/echo")
        {
            Content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))),
        };
        request.Headers.Add(NoCredentialsAuthenticationHandler.UserHeader, "someone");

        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    [Fact]
    public async Task AFormWithoutTheOptionalField_ReachesTheOperation()
    {
        using var response = await PostAsync(("Title", "Harbour"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("Harbour|none|none");
    }

    [Fact]
    public async Task AFormWithTheOptionalField_CarriesIt()
    {
        using var response = await PostAsync(("Title", "Harbour"), ("Caption", "at dawn"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("Harbour|at dawn|none");
    }

    /// <summary>A field declared with a name of its own is read under that name.</summary>
    [Fact]
    public async Task AFieldWithADeclaredName_IsReadUnderThatName()
    {
        using var response = await PostAsync(("Title", "Harbour"), ("note_text", "keep"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("Harbour|none|keep");
    }

    /// <summary>The control: the property's own name is not the key once another is declared.</summary>
    [Fact]
    public async Task AFieldWithADeclaredName_IsNotReadUnderThePropertyName()
    {
        using var response = await PostAsync(("Title", "Harbour"), ("Note", "keep"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("Harbour|none|none");
    }

    /// <summary>The control: the non-nullable field is still refused when absent.</summary>
    [Fact]
    public async Task AFormWithoutTheRequiredField_IsRefused()
    {
        using var response = await PostAsync(("Caption", "at dawn"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
