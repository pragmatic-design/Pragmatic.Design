namespace PragmaticEntities.Tests;

/// <summary>The desk of the request's caller: a scoped service, which a <c>[ComputedDefault]</c> generator may now take (#740).</summary>
public sealed class CallerDesk
{
    public string Name { get; set; } = "";

    public int Priority { get; set; }
}
