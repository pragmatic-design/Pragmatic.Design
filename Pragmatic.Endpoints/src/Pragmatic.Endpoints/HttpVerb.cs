namespace Pragmatic.Endpoints;

/// <summary>
///     HTTP methods supported by endpoints.
/// </summary>
public enum HttpVerb
{
    /// <summary>GET - Retrieve a resource.</summary>
    Get,

    /// <summary>POST - Create a new resource.</summary>
    Post,

    /// <summary>PUT - Replace a resource entirely.</summary>
    Put,

    /// <summary>PATCH - Partially update a resource.</summary>
    Patch,

    /// <summary>DELETE - Remove a resource.</summary>
    Delete,

    /// <summary>HEAD - Like GET but returns headers only, no response body.</summary>
    Head,

    /// <summary>OPTIONS - Describe the communication options for the target resource.</summary>
    Options
}
