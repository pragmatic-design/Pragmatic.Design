using Pragmatic.Result;

namespace Pragmatic.Ensure.Result.Tests;

/// <summary>
///     Simple error type for testing Check methods.
/// </summary>
public sealed record TestError(string Code) : Error
{
    public static TestError Default { get; } = new("TEST_ERROR");

    public override string Code { get; } = Code;

    public override int StatusCode => 400;

    public static implicit operator TestError(string code)
    {
        return new TestError(code);
    }
}