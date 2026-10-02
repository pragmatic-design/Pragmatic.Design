using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Patch.Models;
using Pragmatic.SourceGenerator.Features.Patch.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Patch;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// Verifies hint name convention and basic code generation.
/// </summary>
public class PatchTemplateTests
{
    // ─── PatchTypeTemplate ───────────────────────────────────────────────────

    [Fact]
    public void PatchTypeTemplate_HintName_FollowsVirtualFolderConvention()
    {
        var model = BuildModel("MyApp.Catalog", "UpdatePropertyPatch");

        var artifact = new PatchTypeTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Catalog.UpdatePropertyPatch.Patch.g.cs");
    }

    [Fact]
    public void PatchTypeTemplate_NoNamespace_HintNameStillCorrect()
    {
        var model = BuildModel("", "UpdatePatch");

        var artifact = new PatchTypeTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("UpdatePatch.Patch.g.cs");
    }

    [Fact]
    public void PatchTypeTemplate_GeneratesOptionalProperties()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch", properties:
        [
            new() { Name = "FirstName", TypeFullName = "global::System.String", IsNullable = true }
        ]);

        var source = new PatchTypeTemplate(model).RenderOutput().Text;

        source.Should().Contain("Optional<global::System.String> FirstName");
    }

    [Fact]
    public void PatchTypeTemplate_GeneratesApplyToMethod()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch", entityFullName: "global::MyApp.Guest", properties:
        [
            new() { Name = "FirstName", TypeFullName = "global::System.String" }
        ]);

        var source = new PatchTypeTemplate(model).RenderOutput().Text;

        source.Should().Contain("ApplyTo");
        source.Should().Contain("entity.FirstName = FirstName.Value");
    }

    [Fact]
    public void PatchTypeTemplate_PrivateSetter_UsesSetMethod()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch", entityFullName: "global::MyApp.Guest", properties:
        [
            new() { Name = "Email", TypeFullName = "global::System.String", HasSetMethod = true }
        ]);

        var source = new PatchTypeTemplate(model).RenderOutput().Text;

        source.Should().Contain("entity.SetEmail(Email.Value!)");
    }

    [Fact]
    public void PatchTypeTemplate_GeneratesModifiedProperties()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch", properties:
        [
            new() { Name = "FirstName", TypeFullName = "global::System.String" },
            new() { Name = "LastName", TypeFullName = "global::System.String" }
        ]);

        var source = new PatchTypeTemplate(model).RenderOutput().Text;

        source.Should().Contain("ModifiedProperties");
        source.Should().Contain("set.Add(\"FirstName\")");
        source.Should().Contain("set.Add(\"LastName\")");
    }

    // ─── PatchConverterTemplate ──────────────────────────────────────────────

    [Fact]
    public void PatchConverterTemplate_HintName_FollowsVirtualFolderConvention()
    {
        var model = BuildModel("MyApp.Catalog", "UpdatePropertyPatch");

        var artifact = new PatchConverterTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Catalog.UpdatePropertyPatch.JsonConverter.g.cs");
    }

    [Fact]
    public void PatchConverterTemplate_GeneratesJsonConverterAttribute()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch");

        var source = new PatchConverterTemplate(model).RenderOutput().Text;

        source.Should().Contain("[JsonConverter(typeof(UpdateGuestPatchJsonConverter))]");
    }

    [Fact]
    public void PatchConverterTemplate_GeneratesReadAndWriteMethods()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch", properties:
        [
            new() { Name = "FirstName", TypeFullName = "global::System.String" }
        ]);

        var source = new PatchConverterTemplate(model).RenderOutput().Text;

        source.Should().Contain("public override UpdateGuestPatch Read(");
        source.Should().Contain("public override void Write(");
        source.Should().Contain("Optional<global::System.String>.Of(");

        // ⚠️ This asserted `.Null` for this property, which is not nullable — the shape that let
        // {"firstName": null} through to a NOT NULL column and came back as a database constraint
        // violation. The clear belongs to the nullable case, and the case below is that one.
        source.Should().NotContain("Optional<global::System.String>.Null");
        source.Should().Contain("cannot be cleared");
    }

    /// <summary>And a nullable property still offers all three states.</summary>
    /// <remarks>
    ///     The control on the case above: refusing every null would satisfy it while deleting the
    ///     tri-state the whole feature is.
    /// </remarks>
    [Fact]
    public void PatchConverterTemplate_ANullableProperty_StillOffersTheClear()
    {
        var model = BuildModel("MyApp", "UpdateGuestPatch", properties:
        [
            new() { Name = "Note", TypeFullName = "global::System.String?", IsNullable = true }
        ]);

        var source = new PatchConverterTemplate(model).RenderOutput().Text;

        source.Should().Contain("Optional<global::System.String?>.Null");
        source.Should().NotContain("cannot be cleared");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PatchModel BuildModel(
        string ns,
        string typeName,
        string entityFullName = "global::MyApp.MyEntity",
        ImmutableArray<PatchPropertyModel>? properties = null)
    {
        return new PatchModel
        {
            Namespace = ns,
            TypeName = typeName,
            Accessibility = "public",
            EntityFullName = entityFullName,
            EntityName = "MyEntity",
            Properties = properties ?? ImmutableArray<PatchPropertyModel>.Empty
        };
    }
}
