namespace Pragmatic.Migrations.Cli.ClientGen;

/// <summary>Top-level manifest from SG-generated PragmaticManifest JSON.</summary>
public sealed class ClientManifest
{
    public string? Assembly { get; set; }
    public string? Version { get; set; }
    public List<ClientEndpoint>? Endpoints { get; set; }
    public List<ClientType>? Types { get; set; }
    public List<ClientAction>? Actions { get; set; }
    public List<ClientPermission>? Permissions { get; set; }
}

/// <summary>Endpoint metadata for client generation.</summary>
public sealed class ClientEndpoint
{
    public string? OperationId { get; set; }
    public string? HttpMethod { get; set; }
    public string? FullRoute { get; set; }
    public string? Summary { get; set; }
    public int SuccessStatusCode { get; set; } = 200;
    public bool IsVoid { get; set; }
    public ClientResponse? Response { get; set; }
    public List<ClientParam>? Parameters { get; set; }
    public ClientBody? RequestBody { get; set; }
    public List<ClientError>? Errors { get; set; }
    public ClientAuth? Authorization { get; set; }
    public ClientDomainAction? DomainAction { get; set; }
}

/// <summary>Response type metadata.</summary>
public sealed class ClientResponse
{
    public string? Type { get; set; }
    public bool IsPaged { get; set; }
}

/// <summary>Parameter metadata (route, query, header).</summary>
public sealed class ClientParam
{
    public string? Name { get; set; }
    public string? In { get; set; }
    public string? Type { get; set; }
    public bool IsRequired { get; set; }
}

/// <summary>Request body metadata.</summary>
public sealed class ClientBody
{
    public List<ClientProperty>? Properties { get; set; }
}

/// <summary>Property metadata for request/response bodies.</summary>
public sealed class ClientProperty
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool IsRequired { get; set; }
    public bool IsNullable { get; set; }
    public bool IsEnum { get; set; }
    public int? MaxLength { get; set; }
}

/// <summary>Error response metadata.</summary>
public sealed class ClientError
{
    public string? Type { get; set; }
    public string? Code { get; set; }
    public int StatusCode { get; set; }
}

/// <summary>Authorization metadata.</summary>
public sealed class ClientAuth
{
    public bool AllowAnonymous { get; set; }
    public List<string>? RequiredPermissions { get; set; }
}

/// <summary>Domain action metadata for typed client generation.</summary>
public sealed class ClientDomainAction
{
    public bool IsDomainAction { get; set; }
    public bool IsMutation { get; set; }
    public string? EntityType { get; set; }
    public string? ReturnType { get; set; }
}

/// <summary>Type metadata (entities, enums, errors).</summary>
public sealed class ClientType
{
    public string? Type { get; set; }
    public string? SimpleName { get; set; }
    public string? Kind { get; set; }
    public string? Boundary { get; set; }
    public string? IdType { get; set; }
    public List<ClientProperty>? Properties { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("values")]
    public List<string>? EnumValues { get; set; }

    public string? ErrorCode { get; set; }
    public int? ErrorStatusCode { get; set; }
    public List<ClientExtension>? Extensions { get; set; }
}

/// <summary>Extension property on error types.</summary>
public sealed class ClientExtension
{
    public string? Name { get; set; }
    public string? Type { get; set; }
}

/// <summary>Action metadata for client SDK generation.</summary>
public sealed class ClientAction
{
    public string? Type { get; set; }
    public string? SimpleName { get; set; }
    public string? Kind { get; set; }
    public string? ReturnType { get; set; }
    public string? EntityType { get; set; }
    public string? Boundary { get; set; }
    public bool IsVoid { get; set; }
}

/// <summary>Permission metadata.</summary>
public sealed class ClientPermission
{
    public string? Name { get; set; }
}
