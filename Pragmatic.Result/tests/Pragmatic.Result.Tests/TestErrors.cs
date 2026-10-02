// =============================================================================
// Test Error Types
// =============================================================================

namespace Pragmatic.Result.Tests;

/// <summary>Simple error for testing.</summary>
public sealed record StringError(string Message) : Error
{
    public override string Code => "TEST";
    public override int StatusCode => 400;
}

/// <summary>Validation error for testing.</summary>
public sealed record TestValidationError(string Message) : Error
{
    public override string Code => "VALIDATION";
    public override int StatusCode => 400;
}

/// <summary>Not found error for testing.</summary>
public sealed record TestNotFoundError(string Resource) : Error
{
    public string Message => $"Resource '{Resource}' not found";
    public override string Code => "NOT_FOUND";
    public override int StatusCode => 404;
}

/// <summary>Unauthorized error for testing.</summary>
public sealed record TestUnauthorizedError : Error
{
    public string Message { get; } = "Access denied";
    public override string Code => "UNAUTHORIZED";
    public override int StatusCode => 401;
}