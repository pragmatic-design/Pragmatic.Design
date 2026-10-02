using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The classification rules the privacy diagnostics are decided from.
/// </summary>
/// <remarks>
///     These live on the model rather than in the feature's filtering, because a diagnostic has to be
///     reported <em>against</em> invalid input — dropping it during the transform would leave the user
///     with silence instead of an explanation.
/// </remarks>
public sealed class PersonalDataModelTests
{
    private static PersonalDataModel Model(
        string erasure = "Null", string? reason = null, bool encrypted = false, string category = "Contact")
        => new() { Category = category, Erasure = erasure, Reason = reason, Encrypted = encrypted };

    [Fact]
    public void Retain_WithoutReason_IsFlagged()
        => Model("Retain").IsRetainedWithoutReason.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Retain_WithBlankReason_IsStillFlagged(string reason)
    {
        // A reason that says nothing is the same as no reason: it reaches the processing register and
        // explains nothing to whoever reads it.
        Model("Retain", reason).IsRetainedWithoutReason.Should().BeTrue();
    }

    [Fact]
    public void Retain_WithAReason_IsAccepted()
        => Model("Retain", "Art. 2220 c.c. — ten years").IsRetainedWithoutReason.Should().BeFalse();

    [Fact]
    public void OtherStrategies_AreNeverFlaggedForAMissingReason()
        => Model("Null").IsRetainedWithoutReason.Should().BeFalse();

    [Fact]
    public void DestroyKey_WithoutEncryption_IsFlagged()
        => Model("DestroyKey").IsKeyDestructionWithoutEncryption.Should().BeTrue();

    [Fact]
    public void DestroyKey_WithEncryption_IsAccepted()
        => Model("DestroyKey", encrypted: true).IsKeyDestructionWithoutEncryption.Should().BeFalse();

    [Fact]
    public void EncryptionWithoutDestroyKey_IsNotFlagged()
    {
        // Encrypting a field without erasing by key destruction is a perfectly ordinary choice.
        Model("Null", encrypted: true).IsKeyDestructionWithoutEncryption.Should().BeFalse();
    }

    [Fact]
    public void SpecialCategory_IsRecognised()
    {
        Model(category: "Special").IsSpecialCategory.Should().BeTrue();
        Model(category: "Contact").IsSpecialCategory.Should().BeFalse();
    }

    [Fact]
    public void Model_ComparesByValue()
    {
        // The incremental pipeline caches on this equality; reference equality here would re-render
        // every template on every keystroke.
        Model("Retain", "because").Should().Be(Model("Retain", "because"));
        Model("Retain", "because").Should().NotBe(Model("Retain", "different"));
    }
}
