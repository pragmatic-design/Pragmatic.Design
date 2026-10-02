namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Authorization metadata.</summary>
public sealed class ManifestAuth
{
    public bool AllowAnonymous { get; set; }
    public List<string>? RequiredPermissions { get; set; }
}
