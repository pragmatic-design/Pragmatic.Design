namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     Fully qualified attribute names for Endpoints source generator matching.
///     Note: Endpoint and DomainAction are defined in shared AttributeNames.
/// </summary>
internal static class EndpointAttributeNames
{
    public const string EndpointGroup = "Pragmatic.Endpoints.Attributes.EndpointGroupAttribute";
    // Authorization (new location in Abstractions + legacy in Endpoints)
    public const string RequirePermission = "Pragmatic.Authorization.RequirePermissionAttribute";
    public const string RequireAnyPermission = "Pragmatic.Authorization.RequireAnyPermissionAttribute";
    public const string LegacyRequirePermission = "Pragmatic.Endpoints.Attributes.RequirePermissionAttribute";
    public const string LegacyRequireAnyPermission = "Pragmatic.Endpoints.Attributes.RequireAnyPermissionAttribute";
    public const string RateLimit = "Pragmatic.Endpoints.Attributes.RateLimitAttribute";
    public const string ResponseCache = "Pragmatic.Endpoints.Attributes.ResponseCacheAttribute";
    public const string ApiVersion = "Pragmatic.Endpoints.Attributes.ApiVersionAttribute";
    public const string ApiSummary = "Pragmatic.Endpoints.Attributes.ApiSummaryAttribute";
    public const string ApiDescription = "Pragmatic.Endpoints.Attributes.ApiDescriptionAttribute";
    public const string ApiTags = "Pragmatic.Endpoints.Attributes.ApiTagsAttribute";
    public const string HttpStatus = "Pragmatic.Endpoints.Attributes.HttpStatusAttribute";
    public const string CreatedAt = "Pragmatic.Endpoints.Attributes.CreatedAtAttribute";
    public const string AllowAnonymous = "Pragmatic.Endpoints.Attributes.AllowAnonymousAttribute";

    /// <summary>The route belongs to no tenant, so tenant resolution does not require one.</summary>
    public const string TenantAgnostic = "Pragmatic.Endpoints.Attributes.TenantAgnosticAttribute";

    // Pre/Post processors
    public const string PreProcessor = "Pragmatic.Endpoints.Attributes.PreProcessorAttribute`1";
    public const string PostProcessor = "Pragmatic.Endpoints.Attributes.PostProcessorAttribute`1";

    // Microsoft authorization
    public const string MicrosoftAuthorize = "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";
    public const string MicrosoftAllowAnonymous = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";

    // Autocomplete (for auto-generated autocomplete endpoints)
    public const string Autocomplete = "Pragmatic.Endpoints.Attributes.AutocompleteAttribute";
    public const string AutocompleteGeneric = "Pragmatic.Endpoints.Attributes.AutocompleteAttribute`1";

    // Validation opt-out
    public const string NoValidation = "Pragmatic.Actions.Attributes.NoValidationAttribute";

    // Pragmatic binding attributes
    public const string FromClaim = "Pragmatic.Endpoints.Attributes.FromClaimAttribute";
    public const string FromCookie = "Pragmatic.Endpoints.Attributes.FromCookieAttribute";

    // Versioning
    public const string SinceVersion = "Pragmatic.Endpoints.Attributes.SinceVersionAttribute";

    // ASP.NET Core binding attributes (supported for backward compatibility)
    public const string AspNetFromRoute = "Microsoft.AspNetCore.Mvc.FromRouteAttribute";
    public const string AspNetFromQuery = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";
    public const string AspNetFromHeader = "Microsoft.AspNetCore.Mvc.FromHeaderAttribute";
    public const string AspNetFromBody = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
    public const string AspNetFromForm = "Microsoft.AspNetCore.Mvc.FromFormAttribute";

    // Pragmatic binding attributes (preferred — no ASP.NET Core dependency)
    public const string FromRoute = "Pragmatic.Endpoints.Attributes.FromRouteAttribute";
    public const string FromQuery = "Pragmatic.Endpoints.Attributes.FromQueryAttribute";
    public const string FromHeader = "Pragmatic.Endpoints.Attributes.FromHeaderAttribute";
    public const string FromBody = "Pragmatic.Endpoints.Attributes.FromBodyAttribute";
    public const string FromForm = "Pragmatic.Endpoints.Attributes.FromFormAttribute";

    // File validation attributes
    public const string MaxFileSize = "Pragmatic.Endpoints.Attributes.MaxFileSizeAttribute";
    public const string AllowedContentTypes = "Pragmatic.Endpoints.Attributes.AllowedContentTypesAttribute";

    // Request limits
    public const string MaxBodySize = "Pragmatic.Endpoints.Attributes.MaxBodySizeAttribute";

    // OpenAPI examples
    public const string RequestExample = "Pragmatic.Endpoints.Attributes.RequestExampleAttribute";
    public const string ResponseExample = "Pragmatic.Endpoints.Attributes.ResponseExampleAttribute";

    // Antiforgery
    public const string RequireAntiforgery = "Pragmatic.Endpoints.Attributes.RequireAntiforgeryAttribute";

    // Idempotency
    public const string Idempotent = "Pragmatic.Endpoints.Attributes.IdempotentAttribute";

    // MCP
    public const string McpTool = "Pragmatic.Endpoints.Attributes.McpToolAttribute";
}
