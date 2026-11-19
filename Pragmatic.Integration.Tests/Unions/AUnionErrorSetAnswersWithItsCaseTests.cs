using System.Net;
using System.Text.Json;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>
///     An action whose error type is a C# 15 union answers each case as the arity of the same errors does (#865).
/// </summary>
/// <remarks>
///     <para>
///         Compiled by the real C# 15 compiler: this folder builds on <c>net11.0</c> only, with <c>LangVersion</c> 15. <see cref="PlaceOrderError" />
///         is a <c>partial union</c> that declares <c>IError</c> and writes none of its members, so the build itself is the first assertion:
///         without the generated members it does not compile.
///     </para>
///     <para>
///         The response is built from <c>IError.StatusCode</c> and <c>IError.Title</c>, which the union delegates to its case. The control is
///         <see cref="PlaceOrderArityAction" />, the same three errors returned as themselves.
///     </para>
/// </remarks>
public sealed class AUnionErrorSetAnswersWithItsCaseTests : IAsyncLifetime
{
    private IntegrationTestFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new IntegrationTestFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, "Resource Not Found")]
    [InlineData("taken", HttpStatusCode.Conflict, "Conflict")]
    [InlineData("locked", (HttpStatusCode)423, "Locked")]
    public async Task EachCase_AnswersWithTheStatusAndTitleOfTheArity(string outcome, HttpStatusCode status, string title)
    {
        var union = await _client.PostAsync($"/api/union-orders?outcome={outcome}", null);
        var arity = await _client.PostAsync($"/api/arity-orders?outcome={outcome}", null);

        arity.StatusCode.Should().Be(status, "the control: the error returned as itself");
        union.StatusCode.Should().Be(status, "the union delegates its status to the case it holds");
        (await TitleOf(union)).Should().Be(title, "and its title, which IError's default would leave empty");
        (await TitleOf(union)).Should().Be(await TitleOf(arity));
    }

    [Fact]
    public async Task NoError_IsASuccess()
    {
        var union = await _client.PostAsync("/api/union-orders?outcome=none", null);

        union.IsSuccessStatusCode.Should().BeTrue();
    }

    private static async Task<string?> TitleOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }
}
