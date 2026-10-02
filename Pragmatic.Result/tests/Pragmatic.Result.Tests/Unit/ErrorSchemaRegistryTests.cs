using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class ErrorSchemaRegistryTests
{
    private static ErrorSchemaMetadata SampleMetadata(string code = "TEST", int statusCode = 400)
        => new(code, statusCode, "Test Title", []);

    // =========================================================================
    // Register + TryGet round-trip
    // =========================================================================

    [Fact]
    public void TryGet_AfterRegister_ReturnsTrueWithMetadata()
    {
        ErrorSchemaRegistry.Clear();
        var metadata = SampleMetadata("NOT_FOUND", 404);

        ErrorSchemaRegistry.Register<StringError>(metadata);

        ErrorSchemaRegistry.TryGet(typeof(StringError), out var retrieved).Should().BeTrue();
        retrieved.Should().BeSameAs(metadata);
    }

    [Fact]
    public void TryGet_WithUnregisteredType_ReturnsFalseWithNull()
    {
        ErrorSchemaRegistry.Clear();

        ErrorSchemaRegistry.TryGet(typeof(TestUnauthorizedError), out var retrieved).Should().BeFalse();
        retrieved.Should().BeNull();
    }

    [Fact]
    public void Register_SameTypeTwice_OverwritesPreviousMetadata()
    {
        ErrorSchemaRegistry.Clear();
        ErrorSchemaRegistry.Register<StringError>(SampleMetadata("OLD", 400));
        var newMetadata = SampleMetadata("NEW", 409);

        ErrorSchemaRegistry.Register<StringError>(newMetadata);

        ErrorSchemaRegistry.TryGet(typeof(StringError), out var retrieved).Should().BeTrue();
        retrieved.Should().BeSameAs(newMetadata);
        retrieved!.Code.Should().Be("NEW");
    }

    [Fact]
    public void Register_DifferentTypes_KeepsSeparateMetadata()
    {
        ErrorSchemaRegistry.Clear();
        var stringMeta = SampleMetadata("STRING", 400);
        var validationMeta = SampleMetadata("VALIDATION", 422);

        ErrorSchemaRegistry.Register<StringError>(stringMeta);
        ErrorSchemaRegistry.Register<TestValidationError>(validationMeta);

        ErrorSchemaRegistry.TryGet(typeof(StringError), out var s).Should().BeTrue();
        ErrorSchemaRegistry.TryGet(typeof(TestValidationError), out var v).Should().BeTrue();
        s.Should().BeSameAs(stringMeta);
        v.Should().BeSameAs(validationMeta);
    }

    // =========================================================================
    // Clear
    // =========================================================================

    [Fact]
    public void Clear_RemovesAllRegistrations()
    {
        ErrorSchemaRegistry.Register<StringError>(SampleMetadata());
        ErrorSchemaRegistry.Register<TestValidationError>(SampleMetadata());

        ErrorSchemaRegistry.Clear();

        ErrorSchemaRegistry.TryGet(typeof(StringError), out _).Should().BeFalse();
        ErrorSchemaRegistry.TryGet(typeof(TestValidationError), out _).Should().BeFalse();
    }
}
