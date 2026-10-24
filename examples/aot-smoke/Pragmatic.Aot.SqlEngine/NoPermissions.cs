using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>An authorization that grants nothing.</summary>
internal sealed class NoPermissions : ConstantUserAuthorization
{
    public static readonly NoPermissions Instance = new();

    protected override bool DefaultResult => false;
}
