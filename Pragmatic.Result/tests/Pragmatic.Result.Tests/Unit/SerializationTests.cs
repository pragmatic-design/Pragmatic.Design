using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Serialization;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     Tests for JSON serialization converters: ResultJsonConverter, VoidResultJsonConverter, MaybeJsonConverter.
/// </summary>
public class SerializationTests
{
    // The typed converters, named. Every shape these tests use has a fixed-arity converter, so there
    // is nothing for a run-time factory (MakeGenericType + Activator.CreateInstance) to discover.
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new ResultJsonConverter<int, StringError>(),
            new ResultJsonConverter<string, StringError>(),
            new ResultJsonConverter<string, IError>(),
            new VoidResultJsonConverter<StringError>(),
            new VoidResultJsonConverter<IError>(),
            new MaybeJsonConverter<int>(),
            new MaybeJsonConverter<string>(),
            new MaybeJsonConverter<TestDto>()
        }
    };

    // =========================================================================
    // Result<TValue, TError>
    // =========================================================================

    [Fact]
    public void ResultSuccess_SerializeDeserialize_RoundTrips()
    {
        Result<string, StringError> result = "hello";

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<string, StringError>>(json, Options);

        deserialized.IsSuccess.Should().BeTrue();
        deserialized.Value.Should().Be("hello");
    }

    [Fact]
    public void ResultSuccess_Serialize_ContainsIsSuccessAndValue()
    {
        Result<int, StringError> result = 42;

        var json = JsonSerializer.Serialize(result, Options);

        json.Should().Contain("\"isSuccess\":true");
        json.Should().Contain("\"value\":42");
        json.Should().NotContain("\"error\"");
    }

    [Fact]
    public void ResultFailure_SerializeDeserialize_RoundTrips()
    {
        Result<string, StringError> result = new StringError("something went wrong");

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<string, StringError>>(json, Options);

        deserialized.IsSuccess.Should().BeFalse();
        deserialized.IsFailure.Should().BeTrue();
        deserialized.Error.Message.Should().Be("something went wrong");
    }

    [Fact]
    public void ResultFailure_Serialize_ContainsIsSuccessFalseAndError()
    {
        Result<int, StringError> result = new StringError("fail");

        var json = JsonSerializer.Serialize(result, Options);

        json.Should().Contain("\"isSuccess\":false");
        json.Should().Contain("\"error\"");
        json.Should().NotContain("\"value\"");
    }

    [Fact]
    public void Result_DeserializeMissingIsSuccess_ThrowsJsonException()
    {
        var json = """{"value": "test"}""";

        var act = () => JsonSerializer.Deserialize<Result<string, StringError>>(json, Options);

        act.Should().Throw<JsonException>().WithMessage("*isSuccess*");
    }

    // =========================================================================
    // VoidResult<TError>
    // =========================================================================

    [Fact]
    public void VoidResultSuccess_SerializeDeserialize_RoundTrips()
    {
        var result = VoidResult<StringError>.Success();

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<VoidResult<StringError>>(json, Options);

        deserialized.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void VoidResultSuccess_Serialize_ContainsOnlyIsSuccess()
    {
        var result = VoidResult<StringError>.Success();

        var json = JsonSerializer.Serialize(result, Options);

        json.Should().Contain("\"isSuccess\":true");
        json.Should().NotContain("\"error\"");
    }

    [Fact]
    public void VoidResultFailure_SerializeDeserialize_RoundTrips()
    {
        VoidResult<StringError> result = new StringError("void fail");

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<VoidResult<StringError>>(json, Options);

        deserialized.IsSuccess.Should().BeFalse();
        deserialized.Error.Message.Should().Be("void fail");
    }

    [Fact]
    public void VoidResultFailure_Serialize_ContainsError()
    {
        VoidResult<StringError> result = new StringError("err");

        var json = JsonSerializer.Serialize(result, Options);

        json.Should().Contain("\"isSuccess\":false");
        json.Should().Contain("\"error\"");
    }

    // =========================================================================
    // Polymorphic IError (de)serialization via $errorType discriminator
    // =========================================================================

    [Fact]
    public void ResultFailure_WithIErrorDeclaredType_Serialize_EmitsDiscriminator()
    {
        Result<string, IError> result = Pragmatic.Result.Http.NotFoundError.For("User", "42");

        var json = JsonSerializer.Serialize(result, Options);

        json.Should().Contain("\"$errorType\":\"Pragmatic.Result.Http.NotFoundError\"");
        json.Should().Contain("\"isSuccess\":false");
    }

    [Fact]
    public void ResultFailure_WithIErrorDeclaredType_RoundTrips_ToConcreteType()
    {
        Result<string, IError> result = Pragmatic.Result.Http.NotFoundError.For("User", "42");

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<string, IError>>(json, Options);

        deserialized.IsFailure.Should().BeTrue();
        deserialized.Error.Should().BeOfType<Pragmatic.Result.Http.NotFoundError>();
        deserialized.Error.Code.Should().Be("NOT_FOUND");
        deserialized.Error.StatusCode.Should().Be(404);
    }

    [Fact]
    public void VoidResultFailure_WithIErrorDeclaredType_RoundTrips_ToConcreteType()
    {
        VoidResult<IError> result = Pragmatic.Result.Http.ConflictError.AlreadyExists("User", "42");

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<VoidResult<IError>>(json, Options);

        deserialized.IsFailure.Should().BeTrue();
        deserialized.Error.Should().BeOfType<Pragmatic.Result.Http.ConflictError>();
        deserialized.Error.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public void ResultFailure_WithUnregisteredErrorType_FallsBackToSerializedError()
    {
        // A registered concrete type that we deliberately do NOT register: declare via IError so the
        // write path emits a discriminator the reader cannot resolve.
        Pragmatic.Result.Serialization.ErrorTypeRegistry.Clear();
        Pragmatic.Result.Serialization.ErrorTypeRegistry.RegisterDefaults(); // built-ins only

        Result<string, IError> result = new UnregisteredError("boom");

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<Result<string, IError>>(json, Options);

        deserialized.IsFailure.Should().BeTrue();
        deserialized.Error.Should().BeOfType<Pragmatic.Result.Serialization.SerializedError>();
        deserialized.Error.Code.Should().Be("UNREGISTERED");
        deserialized.Error.StatusCode.Should().Be(418);
    }

    [Fact]
    public void ConcreteErrorType_RoundTrips_Unchanged_WithDiscriminatorPresent()
    {
        // Back-compat: a concrete TError still deserializes directly, ignoring the added discriminator.
        Result<string, StringError> result = new StringError("kept");

        var json = JsonSerializer.Serialize(result, Options);
        json.Should().Contain("\"$errorType\"");

        var deserialized = JsonSerializer.Deserialize<Result<string, StringError>>(json, Options);
        deserialized.Error.Message.Should().Be("kept");
    }

    // =========================================================================
    // Maybe<T> — bare value format
    // =========================================================================

    [Fact]
    public void MaybeSomeInt_Serialize_ProducesBareValue()
    {
        var maybe = Maybe<int>.Some(42);

        var json = JsonSerializer.Serialize(maybe, Options);

        json.Should().Be("42");
    }

    [Fact]
    public void MaybeNoneString_Serialize_ProducesNull()
    {
        var maybe = Maybe<string>.None();

        var json = JsonSerializer.Serialize(maybe, Options);

        json.Should().Be("null");
    }

    [Fact]
    public void MaybeSomeInt_Deserialize_FromBareValue()
    {
        var json = "42";

        var result = JsonSerializer.Deserialize<Maybe<int>>(json, Options);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void MaybeNoneString_Deserialize_FromNull()
    {
        var json = "null";

        var result = JsonSerializer.Deserialize<Maybe<string>>(json, Options);

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void MaybeSomeString_RoundTrip_Preserves()
    {
        var original = Maybe<string>.Some("hello world");

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Maybe<string>>(json, Options);

        deserialized.HasValue.Should().BeTrue();
        deserialized.Value.Should().Be("hello world");
    }

    [Fact]
    public void MaybeSomeComplexObject_RoundTrip_Preserves()
    {
        var dto = new TestDto { Name = "Alice", Age = 30 };
        var original = Maybe<TestDto>.Some(dto);

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Maybe<TestDto>>(json, Options);

        deserialized.HasValue.Should().BeTrue();
        deserialized.Value.Name.Should().Be("Alice");
        deserialized.Value.Age.Should().Be(30);
    }

    [Fact]
    public void MaybeNoneComplexObject_RoundTrip_Preserves()
    {
        var original = Maybe<TestDto>.None();

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Maybe<TestDto>>(json, Options);

        deserialized.HasValue.Should().BeFalse();
    }

    // =========================================================================
    // Helper types
    // =========================================================================

    private sealed class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    /// <summary>An error intentionally never registered, to exercise the SerializedError fallback.</summary>
    private sealed record UnregisteredError(string Detail) : Error
    {
        public override string Code => "UNREGISTERED";
        public override int StatusCode => 418;
    }
}
