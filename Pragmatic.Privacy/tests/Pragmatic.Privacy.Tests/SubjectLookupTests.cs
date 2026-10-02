using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     The two primitives the subject indirection rests on: a reference that reveals nothing, and an
///     index that finds it without storing anything searchable about the person.
/// </summary>
public sealed class SubjectLookupTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void BlindIndex_IsStableForTheSameIdentity()
        => SubjectLookup.BlindIndex(Key, "Customer", "ada@example.com")
            .Should().Equal(SubjectLookup.BlindIndex(Key, "Customer", "ada@example.com"));

    [Fact]
    public void BlindIndex_DiffersBetweenIdentities()
        => SubjectLookup.BlindIndex(Key, "Customer", "ada@example.com")
            .Should().NotEqual(SubjectLookup.BlindIndex(Key, "Customer", "bob@example.com"));

    [Fact]
    public void BlindIndex_SeparatesTheSameIdentityInDifferentRoles()
    {
        // The same person as a customer and as an employee is two relationships. Erasing one must not
        // erase the other, so they cannot share an index.
        SubjectLookup.BlindIndex(Key, "Customer", "ada@example.com")
            .Should().NotEqual(SubjectLookup.BlindIndex(Key, "Employee", "ada@example.com"));
    }

    [Fact]
    public void BlindIndex_CannotBeGamedByMovingCharactersBetweenTypeAndIdentity()
    {
        // Without a length prefix, ("ab","c") and ("a","bc") concatenate identically — one subject type
        // could be made to collide with another identity.
        SubjectLookup.BlindIndex(Key, "ab", "c")
            .Should().NotEqual(SubjectLookup.BlindIndex(Key, "a", "bc"));
    }

    [Fact]
    public void BlindIndex_DependsOnTheKey()
    {
        // Keyed, not plain: a bare hash of an email is recoverable by enumeration, because the space of
        // real addresses is small. The key is what makes a stolen table useless on its own.
        var other = RandomNumberGenerator.GetBytes(32);

        SubjectLookup.BlindIndex(Key, "Customer", "ada@example.com")
            .Should().NotEqual(SubjectLookup.BlindIndex(other, "Customer", "ada@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlindIndex_RejectsABlankIdentity(string? identifier)
    {
        var act = () => SubjectLookup.BlindIndex(Key, "Customer", identifier!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NewReference_IsUnpredictable()
    {
        // Deriving it from the identity would make it reversible by brute force, which would defeat the
        // entire indirection: the trail would be carrying a disguised identity.
        var references = Enumerable.Range(0, 200).Select(_ => SubjectLookup.NewReference()).ToList();

        references.Distinct().Should().HaveCount(200);
    }

    [Fact]
    public void NewReference_IsOpaqueAndCompact()
    {
        var reference = SubjectLookup.NewReference();

        reference.Should().HaveLength(32).And.MatchRegex("^[0-9a-f]+$");
    }
}
