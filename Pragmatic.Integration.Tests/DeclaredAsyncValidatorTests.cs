using System.Net;
using Pragmatic.Integration.Tests.Domain.Validators;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     A <c>[Validator]</c> for an action runs because it is declared, without a second attribute on
///     the action.
/// </summary>
/// <remarks>
///     A mutation found its validator with a runtime lookup, and an action only with <c>[Validate]</c>
///     on the class: the same declaration ran for one kind of operation and was registered and never
///     called for the other. <c>CreateOrderAction</c> carries no <c>[Validate]</c>.
/// </remarks>
public sealed class DeclaredAsyncValidatorTests : IAsyncLifetime
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

    [Fact]
    public async Task PostOrder_WhenTheDeclaredValidatorRefuses_Returns422()
    {
        var response = await _client.PostAsync(
            $"/api/orders?name={ReservedOrderNameValidator.Reserved}&amount=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>The control: a validator that refused everything would pass the first case.</summary>
    [Fact]
    public async Task PostOrder_WhenTheDeclaredValidatorAccepts_Returns201()
    {
        var response = await _client.PostAsync("/api/orders?name=Accepted&amount=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
