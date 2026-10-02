using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     Error handling patterns in domain actions: typed errors, multi-error,
///     implicit conversions, and HTTP status mapping.
/// </summary>
public static class ErrorPatternsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Error Patterns — Typed Errors & HTTP Mapping");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowTypedErrors();
        ShowImplicitConversions();
        ShowMultiErrorActions();

        Console.WriteLine();
    }

    private static void ShowTypedErrors()
    {
        Console.WriteLine("  2.1 Typed errors — Error record with Code, StatusCode, Title");
        Console.WriteLine("  ---------------------------------------------------------------");

        var notFound = new NotFoundError { ResourceType = "Order", ResourceId = "42" };
        Console.WriteLine($"    NotFoundError: Code=\"{notFound.Code}\", Status={notFound.StatusCode}, Title=\"{notFound.Title}\"");
        Console.WriteLine($"                   ResourceType=\"{notFound.ResourceType}\", ResourceId=\"{notFound.ResourceId}\"");

        var validation = new ValidationError { Field = "Email", Message = "Invalid format" };
        Console.WriteLine($"    ValidationError: Code=\"{validation.Code}\", Status={validation.StatusCode}");
        Console.WriteLine($"                     Field=\"{validation.Field}\", Message=\"{validation.Message}\"");
        Console.WriteLine();
    }

    private static void ShowImplicitConversions()
    {
        Console.WriteLine("  2.2 Implicit conversions — return value or error directly");
        Console.WriteLine("  -----------------------------------------------------------");

        Console.WriteLine("""
            // In Execute():
            if (string.IsNullOrWhiteSpace(Product))
                return new ValidationError { ... };  // Implicit: error → Result<Guid, IError>

            return order.Id;  // Implicit: Guid → Result<Guid, IError>
        """);
        Console.WriteLine();

        // Demonstrate the implicit conversion
        Result<Guid, IError> success = Guid.NewGuid();
        Result<Guid, IError> failure = new NotFoundError { ResourceType = "Order", ResourceId = "99" };

        Console.WriteLine($"    Success: IsSuccess={success.IsSuccess}, Value={success.Value}");
        Console.WriteLine($"    Failure: IsFailure={failure.IsFailure}, Error.Code=\"{failure.Error.Code}\"");
        Console.WriteLine();
    }

    private static void ShowMultiErrorActions()
    {
        Console.WriteLine("  2.3 Multi-error actions — up to 6 error types per action");
        Console.WriteLine("  -----------------------------------------------------------");

        Console.WriteLine("""
            // DomainAction with 2 error types:
            public partial class TransferFundsAction
                : DomainAction<TransferResult, ValidationError, InsufficientFundsError>

            // Each error type maps to a different HTTP status:
            //   ValidationError     → 400 Bad Request
            //   InsufficientFundsError → 422 Unprocessable Entity

            // Match handles all error variants exhaustively:
            result.Match(
                transfer => $"Transferred {transfer.Amount}",
                validationError => $"Validation: {validationError.Field}",
                fundsError => $"Insufficient: need {fundsError.Required}"
            );
        """);
        Console.WriteLine();
    }
}
