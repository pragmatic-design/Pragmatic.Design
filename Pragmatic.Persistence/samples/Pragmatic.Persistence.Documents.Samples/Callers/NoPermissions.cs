using Pragmatic.Authorization;

namespace PragmaticDocumentSamples.Callers;

/// <summary>An authorization that grants nothing.</summary>
internal sealed class NoPermissions : ConstantUserAuthorization
{
    public static readonly NoPermissions Instance = new();

    protected override bool DefaultResult => false;
}
