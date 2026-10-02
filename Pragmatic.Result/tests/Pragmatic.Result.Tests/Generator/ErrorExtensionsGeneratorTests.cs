// Pragmatic.Result.Tests - Unified Generator: WriteExtensions
// Regression coverage for #41: the generated partial must agree with the user's
// record declaration (CS0261 otherwise).

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Generator;

public class ErrorExtensionsGeneratorTests : UnifiedResultGeneratorTestBase
{
    // Regression for #41: before the record-aware fix the generator emitted
    // `partial class RoomError` for a `partial record RoomError : Error`, which
    // fails to compile with CS0261 in every consumer that follows the documented
    // sealed-partial-record pattern.
    [Fact]
    public void WriteExtensions_PartialRecordError_CompilesAndEmitsPartialRecord()
    {
        const string source = """
            using Pragmatic.Result;
            using Pragmatic.Result.Http;
            namespace Demo;
            public sealed partial record RoomError : Error
            {
                public override string Code => "ROOM";
                public override int StatusCode => 409;
                public System.Guid RoomId { get; init; }
            }
            """;

        var result = RunUnifiedGenerator(source);

        // A generated partial that disagrees with the record declaration fails with CS0261.
        HasCompilationErrors(result).Should().BeFalse(
            because: "generated partial must agree with the record declaration: {0}",
            string.Join("; ", GetCompilationErrors(result).Select(d => d.GetMessage())));

        var generated = GetGeneratedSource(result, "ErrorExtensions");
        generated.Should().NotBeNull();
        generated.Should().Contain("partial record RoomError");
        generated.Should().NotContain("partial class RoomError");
        generated.Should().Contain("WriteExtensions");
        generated.Should().Contain("extensions[\"roomId\"]");
    }

    // A nullable reference property must be null-guarded before being written.
    [Fact]
    public void WriteExtensions_NullableReferenceProperty_EmitsNullGuard()
    {
        const string source = """
            using Pragmatic.Result;
            namespace Demo;
            public sealed partial record TenantError : Error
            {
                public override string Code => "TENANT";
                public override int StatusCode => 400;
                public string? TenantName { get; init; }
            }
            """;

        var result = RunUnifiedGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            because: string.Join("; ", GetCompilationErrors(result).Select(d => d.GetMessage())));

        var generated = GetGeneratedSource(result, "ErrorExtensions");
        generated.Should().NotBeNull();
        generated.Should().Contain("partial record TenantError");
        generated.Should().Contain("TenantName is not null");
        generated.Should().Contain("extensions[\"tenantName\"]");
    }

    // #28: a [JsonIgnore] property is the opt-out for keeping a custom error property off the wire —
    // it must NOT be emitted into WriteExtensions (and is symmetrically excluded from the OpenAPI schema).
    [Fact]
    public void WriteExtensions_JsonIgnoreProperty_IsExcluded()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using Pragmatic.Result;
            namespace Demo;
            public sealed partial record PaymentError : Error
            {
                public override string Code => "PAYMENT";
                public override int StatusCode => 402;
                public string PublicReason { get; init; } = "";
                [JsonIgnore]
                public string SecretToken { get; init; } = "";
            }
            """;

        var result = RunUnifiedGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            because: string.Join("; ", GetCompilationErrors(result).Select(d => d.GetMessage())));

        var generated = GetGeneratedSource(result, "ErrorExtensions");
        generated.Should().NotBeNull();
        // Public property is on the wire.
        generated.Should().Contain("extensions[\"publicReason\"]");
        // [JsonIgnore] property must be absent — neither the extension key nor the member reference.
        generated.Should().NotContain("secretToken");
        generated.Should().NotContain("SecretToken");
    }

    // An IError-only record struct does not extend Error, so no WriteExtensions is
    // emitted — but the generator must not crash or produce invalid code.
    [Fact]
    public void WriteExtensions_IErrorOnlyRecordStruct_CompilesWithoutOutput()
    {
        const string source = """
            using Pragmatic.Result;
            namespace Demo;
            public readonly partial record struct LiteError : IError
            {
                public string Code => "LITE";
                public int StatusCode => 400;
                public string Title => "Lite";
                public string? Description => null;
                public int Attempt { get; init; }
            }
            """;

        var result = RunUnifiedGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            because: string.Join("; ", GetCompilationErrors(result).Select(d => d.GetMessage())));

        // Not extending Error → no WriteExtensions override generated.
        GetGeneratedSource(result, "ErrorExtensions").Should().BeNull();
    }
}
