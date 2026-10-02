using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class ErrorSchemaMetadataTests
{
    // =========================================================================
    // ErrorSchemaMetadata record
    // =========================================================================

    [Fact]
    public void ErrorSchemaMetadata_ExposesConstructorArguments()
    {
        var properties = new[]
        {
            new ErrorPropertyDescriptor("field", "string", "The offending field"),
        };

        var metadata = new ErrorSchemaMetadata("NOT_FOUND", 404, "Not Found", properties);

        metadata.Code.Should().Be("NOT_FOUND");
        metadata.StatusCode.Should().Be(404);
        metadata.Title.Should().Be("Not Found");
        metadata.ExtensionProperties.Should().BeSameAs(properties);
    }

    [Fact]
    public void ErrorSchemaMetadata_WithSameValues_AreEqual()
    {
        IReadOnlyList<ErrorPropertyDescriptor> props = [];
        var a = new ErrorSchemaMetadata("CODE", 400, "Title", props);
        var b = new ErrorSchemaMetadata("CODE", 400, "Title", props);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ErrorSchemaMetadata_WithDifferentCode_AreNotEqual()
    {
        IReadOnlyList<ErrorPropertyDescriptor> props = [];
        var a = new ErrorSchemaMetadata("CODE_A", 400, "Title", props);
        var b = new ErrorSchemaMetadata("CODE_B", 400, "Title", props);

        a.Should().NotBe(b);
    }

    // =========================================================================
    // ErrorPropertyDescriptor readonly record struct
    // =========================================================================

    [Fact]
    public void ErrorPropertyDescriptor_ExposesConstructorArguments()
    {
        var enumValues = new[] { "A", "B" };

        var descriptor = new ErrorPropertyDescriptor(
            "statusCode", "integer", "The status", IsNullable: true, EnumValues: enumValues);

        descriptor.CamelCaseName.Should().Be("statusCode");
        descriptor.JsonSchemaType.Should().Be("integer");
        descriptor.Description.Should().Be("The status");
        descriptor.IsNullable.Should().BeTrue();
        descriptor.EnumValues.Should().BeSameAs(enumValues);
    }

    [Fact]
    public void ErrorPropertyDescriptor_OptionalArguments_DefaultToNonNullableAndNullEnum()
    {
        var descriptor = new ErrorPropertyDescriptor("name", "string", "A name");

        descriptor.IsNullable.Should().BeFalse();
        descriptor.EnumValues.Should().BeNull();
    }

    [Fact]
    public void ErrorPropertyDescriptor_WithSameValues_AreEqual()
    {
        var a = new ErrorPropertyDescriptor("field", "string", "desc");
        var b = new ErrorPropertyDescriptor("field", "string", "desc");

        a.Should().Be(b);
    }
}
