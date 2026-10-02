using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Http;
using Pragmatic.Result.Serialization;
using Generated = Pragmatic.Result.Tests.Generated;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     JSON round-trip tests for the untyped <see cref="Result{TValue}" /> variant and the
///     source-generated multi-error variants. Without a converter, System.Text.Json touches the
///     throwing <c>Value</c>/<c>Error</c> members via reflection and throws
///     <see cref="InvalidOperationException" />.
/// </summary>
public class MultiErrorSerializationTests
{
    // The converters the generator emits for the contracts declared in JsonResultContracts.cs, plus the
    // fixed-arity one for the untyped variant. No converter is picked at run time with
    // MakeGenericType, so these tests exercise the generated code, not a reflective shim.
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new UntypedResultJsonConverter<int>(),
            new UntypedResultJsonConverter<string>(),
            new Generated.ResultInt32NotFoundErrorConflictErrorJsonConverter(),
            new Generated.ResultInt32BadRequestErrorNotFoundErrorConflictErrorJsonConverter(),
            new Generated.ResultInt32UnregisteredErrorConflictErrorJsonConverter(),
            new Generated.VoidResultNotFoundErrorConflictErrorJsonConverter()
        }
    };

    // =========================================================================
    // Repro for finding #29 — serialization must not throw
    // =========================================================================

    [Fact]
    public void Repro29_SerializeUntypedResult_DoesNotThrow()
    {
        var act = () => JsonSerializer.Serialize(Result<int>.Success(1), Options);

        act.Should().NotThrow();
    }

    [Fact]
    public void Repro29_SerializeMultiErrorResult_DoesNotThrow()
    {
        var success = Result<int, NotFoundError, ConflictError>.Success(1);

        var act = () => JsonSerializer.Serialize(success, Options);

        act.Should().NotThrow();
    }

    // =========================================================================
    // Untyped Result<T>
    // =========================================================================

    [Fact]
    public void UntypedResult_Success_RoundTrips()
    {
        var result = Result<int>.Success(42);

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<int>>(json, Options);

        json.Should().Contain("\"isSuccess\":true").And.Contain("\"value\":42");
        deserialized.IsSuccess.Should().BeTrue();
        deserialized.Value.Should().Be(42);
    }

    [Fact]
    public void UntypedResult_SuccessWithNullValue_RoundTrips()
    {
        var result = Result<string?>.Success(null);

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<string?>>(json, Options);

        deserialized.IsSuccess.Should().BeTrue();
        deserialized.Value.Should().BeNull();
    }

    [Fact]
    public void UntypedResult_Failure_RoundTripsToConcreteType()
    {
        var result = Result<int>.Failure(NotFoundError.For("User", "42"));

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<int>>(json, Options);

        json.Should().Contain("\"$errorType\":\"Pragmatic.Result.Http.NotFoundError\"");
        deserialized.IsFailure.Should().BeTrue();
        deserialized.Error.Should().BeOfType<NotFoundError>();
        deserialized.Error.Code.Should().Be("NOT_FOUND");
        deserialized.Error.StatusCode.Should().Be(404);
    }

    [Fact]
    public void UntypedResult_FailureWithUnregisteredError_FallsBackToSerializedError()
    {
        ErrorTypeRegistry.Clear();
        ErrorTypeRegistry.RegisterDefaults();

        var result = Result<int>.Failure(new UnregisteredError("boom"));

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<int>>(json, Options);

        deserialized.IsFailure.Should().BeTrue();
        deserialized.Error.Should().BeOfType<SerializedError>();
        deserialized.Error.Code.Should().Be("UNREGISTERED");
        deserialized.Error.StatusCode.Should().Be(418);
    }

    // =========================================================================
    // Multi-error Result<T, E1, E2>
    // =========================================================================

    [Fact]
    public void MultiErrorResult_Success_RoundTrips()
    {
        var result = Result<int, NotFoundError, ConflictError>.Success(7);

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<int, NotFoundError, ConflictError>>(json, Options);

        json.Should().Contain("\"isSuccess\":true").And.Contain("\"value\":7");
        deserialized.IsSuccess.Should().BeTrue();
        deserialized.Value.Should().Be(7);
    }

    [Fact]
    public void MultiErrorResult_FailureWithFirstError_PreservesConcreteType()
    {
        var result = Result<int, NotFoundError, ConflictError>.Failure(NotFoundError.For("User", "42"));

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<int, NotFoundError, ConflictError>>(json, Options);

        json.Should().Contain("\"$errorType\":\"Pragmatic.Result.Http.NotFoundError\"");
        deserialized.IsFailure.Should().BeTrue();
        deserialized.TryGetError1(out var error).Should().BeTrue();
        error.Should().BeOfType<NotFoundError>();
        error!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void MultiErrorResult_FailureWithSecondError_PreservesConcreteType()
    {
        var result = Result<int, NotFoundError, ConflictError>.Failure(ConflictError.AlreadyExists("User", "42"));

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<int, NotFoundError, ConflictError>>(json, Options);

        json.Should().Contain("\"$errorType\":\"Pragmatic.Result.Http.ConflictError\"");
        deserialized.IsFailure.Should().BeTrue();
        deserialized.TryGetError2(out var error).Should().BeTrue();
        error.Should().BeOfType<ConflictError>();
        error!.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public void MultiErrorResult_ErrorInADeclaredSlot_RoundTripsWithoutRegistration()
    {
        // The generated converter switches over the slots the result declares, so resolution does not
        // depend on anyone having called ErrorTypeRegistry.Register for the type. Resolved through the
        // registry alone, this payload would come back as SerializedError and then be rejected.
        ErrorTypeRegistry.Clear();
        ErrorTypeRegistry.RegisterDefaults();

        var result = Result<int, UnregisteredError, ConflictError>.Failure(new UnregisteredError("boom"));
        var json = JsonSerializer.Serialize(result, Options);

        var deserialized = JsonSerializer.Deserialize<Result<int, UnregisteredError, ConflictError>>(json, Options);

        deserialized.IsFailure.Should().BeTrue();
        deserialized.TryGetError1(out var error).Should().BeTrue();
        error.Should().BeOfType<UnregisteredError>();
    }

    [Fact]
    public void MultiErrorResult_ErrorOutsideTheDeclaredSlots_ThrowsJsonException()
    {
        // A payload naming a type the result does not declare. Hand-written rather than produced by
        // serializing one, because the writer can only emit a declared slot — which is the point.
        const string Json = """
            {"isSuccess":false,"error":{"$errorType":"Some.Other.Error","code":"NOPE","statusCode":400}}
            """;

        var act = () => JsonSerializer.Deserialize<Result<int, UnregisteredError, ConflictError>>(Json, Options);

        act.Should().Throw<JsonException>();
        act.Should().NotThrow<InvalidOperationException>();
    }

    [Fact]
    public void MultiErrorResult_HigherArity_RoundTrips()
    {
        var result = Result<int, BadRequestError, NotFoundError, ConflictError>
            .Failure(NotFoundError.For("Order", "9"));

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer
            .Deserialize<Result<int, BadRequestError, NotFoundError, ConflictError>>(json, Options);

        deserialized.IsFailure.Should().BeTrue();
        deserialized.TryGetError2(out var error).Should().BeTrue();
        error.Should().BeOfType<NotFoundError>();
    }

    // =========================================================================
    // Multi-error VoidResult<E1, E2>
    // =========================================================================

    [Fact]
    public void MultiErrorVoidResult_Success_RoundTrips()
    {
        var result = VoidResult<NotFoundError, ConflictError>.Success();

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<VoidResult<NotFoundError, ConflictError>>(json, Options);

        json.Should().Contain("\"isSuccess\":true").And.NotContain("\"error\"");
        deserialized.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void MultiErrorVoidResult_Failure_PreservesConcreteType()
    {
        var result = VoidResult<NotFoundError, ConflictError>.Failure(ConflictError.AlreadyExists("User", "42"));

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<VoidResult<NotFoundError, ConflictError>>(json, Options);

        json.Should().Contain("\"$errorType\":\"Pragmatic.Result.Http.ConflictError\"");
        deserialized.IsFailure.Should().BeTrue();
        deserialized.TryGetError2(out var error).Should().BeTrue();
        error.Should().BeOfType<ConflictError>();
    }

}

/// <summary>An error intentionally never registered, to exercise the fallback paths.</summary>
/// <remarks>
///     Top-level and internal because <c>[assembly: JsonResultContract&lt;…&gt;]</c> has to name it, and an
///     assembly attribute cannot reach a private nested type.
/// </remarks>
internal sealed record UnregisteredError(string Detail) : Error
{
    public override string Code => "UNREGISTERED";
    public override int StatusCode => 418;
}
