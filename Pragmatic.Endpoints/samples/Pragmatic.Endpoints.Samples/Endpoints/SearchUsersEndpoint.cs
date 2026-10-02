using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Samples.Models;
using Pragmatic.Endpoints.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Searches users with various filters.
///     Demonstrates query parameters (required and optional) plus DI with ILogger.
/// </summary>
[Endpoint(HttpVerb.Get, "/users/search")]
[ApiSummary("Search Users")]
[ApiDescription("Searches users with various filter options.")]
[ApiTags("Users")]
public partial class SearchUsersEndpoint : Endpoint<UserResponse[]>
{
    private ILogger<SearchUsersEndpoint> _logger = null!;

    // DI dependencies (not readonly for SetDependencies)
    private IUserRepository _repository = null!;

    /// <summary>
    ///     Search term to filter by name or email (required).
    /// </summary>
    [FromQuery]
    public required string Query { get; set; }

    /// <summary>
    ///     Page number for pagination (optional, defaults to 1).
    /// </summary>
    [FromQuery]
    public int Page { get; set; } = 1;

    /// <summary>
    ///     Page size for pagination (optional, defaults to 10).
    /// </summary>
    [FromQuery]
    public int PageSize { get; set; } = 10;

    /// <summary>
    ///     Optional filter by email domain.
    /// </summary>
    [FromQuery]
    public string? Domain { get; set; }

    /// <summary>
    ///     Optional client identifier for tracking.
    /// </summary>
    [FromHeader(Name = "X-Client-Id")]
    public string? ClientId { get; set; }

    /// <inheritdoc />
    public override async Task<Result<UserResponse[]>> HandleAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Searching users with query '{Query}', page {Page}, pageSize {PageSize}, domain {Domain}, clientId {ClientId}",
            Query, Page, PageSize, Domain, ClientId);

        var users = await _repository.GetAllAsync(Page, PageSize, ct);

        // Apply search filter (in real app this would be in repository)
        var filtered = users
            .Where(u => u.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                        u.Email.Contains(Query, StringComparison.OrdinalIgnoreCase))
            .Where(u => string.IsNullOrEmpty(Domain) || u.Email.EndsWith($"@{Domain}"))
            .Select(u => new UserResponse
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                CreatedAt = u.CreatedAt
            })
            .ToArray();

        return filtered;
    }
}