namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     An error whose custom properties start with an acronym, compiled with the unified generator so its
///     <c>WriteExtensions</c> is the generated one.
/// </summary>
public sealed partial record AcronymLedError : Error
{
    public override string Code => "ACRONYM_LED";
    public override int StatusCode => 400;
    public string URL { get; init; } = "";
    public string IOStream { get; init; } = "";
}
