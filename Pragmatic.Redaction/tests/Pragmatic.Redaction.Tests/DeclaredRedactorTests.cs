using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Redaction.Tests;

/// <summary>
///     <c>[NotLogged]</c> marks members, and the redactor is what reads the marking. The JSON log
///     providers serialize every complex structured property whole — <c>SerializeComplexObjects</c>
///     defaults to <see langword="true" /> — so without it
///     <c>logger.LogInformation("handling {Message}", message)</c> puts the entire graph in the log,
///     marked members included: the accidental gesture the attribute exists for.
/// </summary>
public class DeclaredRedactorTests
{
    private sealed record Payload(string Reference, string NegotiatedRate);

    /// <summary>A stand-in for the map the generator emits per module assembly.</summary>
    private sealed class Map(Type type, params string[] properties) : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type t, out IReadOnlyList<RedactedMember> members)
        {
            members = t == type
                ? [.. properties.Select(n => new RedactedMember(n, RedactionReason.NotLogged))]
                : [];
            return members.Count > 0;
        }
    }

    private static readonly Payload Sample = new("PO-4471", "0.82");

    [Fact]
    public void ADeclaredMember_IsMasked()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Payload), "NegotiatedRate")]);

        var json = redactor.Serialize(Sample);

        json.Should().NotContain("0.82").And.Contain(PersonalDataPatterns.Mask);
    }

    /// <summary>
    ///     Masked, not omitted: a missing key reads as "the field was not set", which is a different
    ///     statement about what happened, and a false one.
    /// </summary>
    [Fact]
    public void TheKeySurvives_OnlyItsValueGoes()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Payload), "NegotiatedRate")]);

        redactor.Serialize(Sample).Should().Contain("NegotiatedRate");
    }

    [Fact]
    public void WhatWasNotDeclared_IsUntouched()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Payload), "NegotiatedRate")]);

        redactor.Serialize(Sample).Should().Contain("PO-4471");
    }

    /// <summary>
    ///     The case that makes this a live leak rather than a dead channel: with no map — which is
    ///     what an application that declares nothing has — the graph goes out whole.
    /// </summary>
    [Fact]
    public void WithNoMapAtAll_NothingIsMasked_WhichIsThePointOfTheDefect()
    {
        var redactor = new DeclaredRedactor([]);

        redactor.Serialize(Sample).Should().Contain("0.82");
    }

    [Fact]
    public void AMapThatDoesNotKnowTheType_LeavesItAlone()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(string), "Anything")]);

        redactor.Serialize(Sample).Should().Contain("0.82");
    }

    [Fact]
    public void Null_IsNotAnError()
    {
        new DeclaredRedactor([]).Serialize(null).Should().Be("null");
    }

    /// <summary>
    ///     Without <c>[JsonPropertyName]</c> the map carries the CLR name in PascalCase
    ///     while the payload serializes camelCase. An ordinal comparison would mask nothing and say
    ///     so to nobody.
    /// </summary>
    [Fact]
    public void TheNameIsMatchedCaseInsensitively()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Payload), "negotiatedrate")]);

        redactor.Serialize(Sample).Should().NotContain("0.82");
    }

    /// <summary>
    ///     A member declared through <c>[PersonalData]</c> travels the same channel and carries its
    ///     category, so the output can tell the two apart later without re-splitting the map.
    /// </summary>
    [Fact]
    public void APersonalDataMemberIsRedactedToo_AndKeepsItsReason()
    {
        var map = new PersonalMap(typeof(Payload), new RedactedMember("NegotiatedRate", RedactionReason.PersonalData, "financial"));
        var redactor = new DeclaredRedactor([map]);

        redactor.Serialize(Sample).Should().NotContain("0.82");
        redactor.TryGetRedactedMembers(typeof(Payload), out var members).Should().BeTrue();
        members[0].Reason.Should().Be(RedactionReason.PersonalData);
        members[0].Category.Should().Be("financial");
    }

    private sealed class PersonalMap(Type type, params RedactedMember[] members) : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type t, out IReadOnlyList<RedactedMember> found)
        {
            found = t == type ? members : [];
            return found.Count > 0;
        }
    }

    // =========================================================================
    // What a declaration one level down is worth
    // =========================================================================

    private sealed record Credentials(string Email, string PasswordHash);

    private sealed record Person(string Name, Credentials Identity, string HiredOn);

    private sealed record Team(string Name, IReadOnlyList<Credentials> Members);

    private static readonly Person Employee =
        new("A. Applicant", new Credentials("a@example.test", "$2a$hash"), "2024-03-01");

    /// <summary>
    ///     A member classified on a nested type is masked where it sits.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A walk over the <b>top-level key set</b> of the payload, consulting the map for the
    ///     outermost type alone, would let an owned record carry whatever it holds out in clear — the
    ///     framework's own <c>LocalIdentity</c> with the sign-in address beside the password hash, the
    ///     reset token and the security stamp. The map carries the path, because the generator knows
    ///     the shape and the payload does not carry a type for the redactor to look one up by.
    /// </remarks>
    [Fact]
    public void AMemberDeclaredOnANestedType_IsMaskedWhereItSits()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Person), "Identity.Email")]);

        var json = redactor.Serialize(Employee);

        json.Should().NotContain("a@example.test");
        json.Should().Contain(PersonalDataPatterns.Mask);
    }

    /// <summary>
    ///     The control: what nobody declared, at any depth, still goes out.
    /// </summary>
    /// <remarks>
    ///     "Nothing leaks" is satisfied perfectly by a redactor that masks the whole graph, which
    ///     would leave a log entry saying nothing at all.
    /// </remarks>
    [Fact]
    public void WhatIsNotDeclaredAtDepth_IsStillWritten()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Person), "Identity.Email")]);

        var json = redactor.Serialize(Employee);

        json.Should().Contain("2024-03-01", "HiredOn declares nothing");
        json.Should().Contain("$2a$hash", "and neither does PasswordHash in this map");
    }

    /// <summary>
    ///     A classified member inside a collection is masked in every element.
    /// </summary>
    /// <remarks>
    ///     The second half of the same blind spot: a list of classified children was as exposed as a
    ///     single owned record, and one element masked would be worse than none — it reads as though
    ///     the mechanism ran.
    /// </remarks>
    [Fact]
    public void AClassifiedMemberInsideACollection_IsMaskedInEveryElement()
    {
        var team = new Team("Payroll", [
            new Credentials("first@example.test", "h1"),
            new Credentials("second@example.test", "h2"),
        ]);
        var redactor = new DeclaredRedactor([new Map(typeof(Team), "Members[].Email")]);

        var json = redactor.Serialize(team);

        json.Should().NotContain("first@example.test");
        json.Should().NotContain("second@example.test");
        json.Should().Contain("h1").And.Contain("h2", "the hashes are not in this map");
    }

    /// <summary>
    ///     A path that does not match the payload leaves it alone rather than throwing.
    /// </summary>
    /// <remarks>
    ///     A log entry is written on a path where an exception is the worst possible answer: the
    ///     redactor runs inside the logger, so throwing would lose the entry it was protecting.
    /// </remarks>
    [Fact]
    public void APathThePayloadDoesNotHave_IsNotAnError()
    {
        var redactor = new DeclaredRedactor([new Map(typeof(Person), "Identity.Nothing.Here")]);

        redactor.Serialize(Employee).Should().Contain("a@example.test");
    }
}
