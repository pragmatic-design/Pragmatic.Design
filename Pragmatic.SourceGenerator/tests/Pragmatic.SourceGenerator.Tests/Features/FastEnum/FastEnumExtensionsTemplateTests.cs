using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.FastEnum.Models;
using Pragmatic.SourceGenerator.Features.FastEnum.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.FastEnum;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// </summary>
public class FastEnumExtensionsTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_FollowsConvention()
    {
        var model = BuildModel("MyApp.Domain", "Status");

        var artifact = new FastEnumTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Domain.Status.FastEnum.g.cs");
    }

    [Fact]
    public void RenderOutput_GlobalNamespace_HintName_OmitsNamespace()
    {
        var model = BuildModel("", "Status");

        var artifact = new FastEnumTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("Status.FastEnum.g.cs");
    }

    [Fact]
    public void RenderOutput_SameSimpleNameDifferentNamespaces_ProducesDistinctHintNames()
    {
        // Two [FastEnum] enums with the same simple name in different namespaces must not
        // collide on the AddSource hint (Roslyn requires unique hint names per generator run).
        var sales = new FastEnumTemplate(BuildModel("Sales", "Status")).RenderOutput();
        var billing = new FastEnumTemplate(BuildModel("Billing", "Status")).RenderOutput();

        sales.HintName.Should().Be("Sales.Status.FastEnum.g.cs");
        billing.HintName.Should().Be("Billing.Status.FastEnum.g.cs");
        sales.HintName.Should().NotBe(billing.HintName);
    }

    [Fact]
    public void RenderOutput_GeneratesExtensionsAndJsonConverterClasses()
    {
        var model = BuildModel("MyApp", "Status");

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("public static class StatusExtensions");
        source.Should().Contain("public sealed class StatusJsonConverter");
    }

    [Fact]
    public void RenderOutput_JsonConverter_ReportsFailureWithTheFrameworkWording()
    {
        // The old message interpolated the token: reading `null` produced "Unable to convert '' to
        // Status.", which claims an empty string was seen. The equivalence test compares exception
        // TYPES, so only this assertion pins the wording.
        var model = BuildModel("MyApp", "Status");

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("\"The JSON value could not be converted to Status.\"");
        source.Should().NotContain("Unable to convert");
    }

    [Fact]
    public void RenderOutput_ToStringFast_EmitsSwitchArmPerMember()
    {
        var model = BuildModel("MyApp", "Status", members:
        [
            new() { Name = "Active", Value = "0" },
            new() { Name = "Inactive", Value = "1" }
        ]);

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("public static string ToStringFast(this Status value)");
        source.Should().Contain("Status.Active => nameof(Status.Active),");
        source.Should().Contain("Status.Inactive => nameof(Status.Inactive),");
    }

    [Fact]
    public void RenderOutput_Count_ReflectsMemberCount()
    {
        var model = BuildModel("MyApp", "Status", members:
        [
            new() { Name = "Active", Value = "0" },
            new() { Name = "Inactive", Value = "1" }
        ]);

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("public static int Count => 2;");
    }

    [Fact]
    public void RenderOutput_WithDescription_GeneratesGetDisplayNameWithLiteral()
    {
        // GetDisplayName is only emitted when at least one member has a DisplayName.
        var model = BuildModel("MyApp", "Status", members:
        [
            new() { Name = "Active", Value = "0", DisplayName = "Currently Active" }
        ]);

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("public static string GetDisplayName(this Status value)");
        source.Should().Contain("Status.Active => \"Currently Active\",");
    }

    [Fact]
    public void RenderOutput_MemberWithoutDescription_GetDisplayNameFallsBackToNameof()
    {
        // Without a DisplayName the GetDisplayName arm falls back to nameof(...) for that member.
        var model = BuildModel("MyApp", "Status", members:
        [
            new() { Name = "Active", Value = "0" }
        ]);

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("Status.Active => nameof(Status.Active),");
        source.Should().NotContain("Status.Active => \"Active\",");
    }

    [Fact]
    public void RenderOutput_GetValuesAndGetNames_UseCachedBackingArrays()
    {
        // The ReadOnlySpan signature promises zero-alloc: `=> new[] {...}` would heap-allocate
        // on every call. Both accessors must return a single static readonly backing array.
        var model = BuildModel("MyApp", "Status", members:
        [
            new() { Name = "Active", Value = "0" },
            new() { Name = "Inactive", Value = "1" }
        ]);

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("private static readonly Status[] _values = { Status.Active, Status.Inactive };");
        source.Should().Contain("public static ReadOnlySpan<Status> GetValues() => _values;");
        source.Should().Contain("private static readonly string[] _names = { nameof(Status.Active), nameof(Status.Inactive) };");
        source.Should().Contain("public static ReadOnlySpan<string> GetNames() => _names;");
        source.Should().NotContain("GetValues() => new[]");
        source.Should().NotContain("GetNames() => new[]");
    }

    [Fact]
    public void RenderOutput_InvalidModel_DoesNotRenderExtensionsClass()
    {
        // IsValid=false → Validate() fails → header-only, no class body.
        var model = BuildModel("MyApp", "Empty", members: [], isValid: false);

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().NotContain("class EmptyExtensions");
        source.Should().NotContain("ToStringFast");
    }

    // C# allows two fields to share a value — Success = 0, Ok = 0 — and the second is an alias. One
    // switch arm per field then emits the same constant pattern twice and the generated file does not
    // compile, with nothing warning first.
    [Fact]
    public void RenderOutput_AliasedMembers_EmitsOneSwitchArmPerValue()
    {
        var model = BuildModel("MyApp", "Outcome", ImmutableArray.Create(
            new FastEnumMemberModel { Name = "Success", Value = "0" },
            new FastEnumMemberModel { Name = "Ok", Value = "0" },
            new FastEnumMemberModel { Name = "Failure", Value = "1" }));

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        // First declaration wins, which is also the name Enum.ToString() returns for an alias.
        source.Should().Contain("Outcome.Success => nameof(Outcome.Success)");
        source.Should().NotContain("Outcome.Ok => nameof(Outcome.Ok)",
            "an alias shares the constant, so a second arm for it would be a duplicate pattern");
        source.Should().Contain("Outcome.Success => true").And.Contain("Outcome.Failure => true");
        source.Should().NotContain("Outcome.Ok => true");
    }

    // The alias is still a real name: it has to parse, and it has to be reported as defined. Only the
    // switches on the VALUE collapse.
    [Fact]
    public void RenderOutput_AliasedMembers_KeepsEveryNameInTheNameSwitches()
    {
        var model = BuildModel("MyApp", "Outcome", ImmutableArray.Create(
            new FastEnumMemberModel { Name = "Success", Value = "0" },
            new FastEnumMemberModel { Name = "Ok", Value = "0" }));

        var source = new FastEnumTemplate(model).RenderOutput().Text;

        source.Should().Contain("nameof(Outcome.Ok) => true",
            "IsDefined(string) switches on the name, and the alias is a defined name");
    }

    private static FastEnumModel BuildModel(
        string ns,
        string typeName,
        ImmutableArray<FastEnumMemberModel>? members = null,
        bool isValid = true) => new()
        {
            TypeName = typeName,
            FullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}",
            Namespace = ns,
            Accessibility = "public",
            UnderlyingType = "int",
            Members = members ?? ImmutableArray.Create(new FastEnumMemberModel { Name = "Default", Value = "0" }),
            IsValid = isValid && (members is null || members.Value.Length > 0)
        };
}
