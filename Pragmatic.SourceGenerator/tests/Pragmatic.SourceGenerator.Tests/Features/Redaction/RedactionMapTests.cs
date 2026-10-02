using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Redaction;

/// <summary>
///     The declared-redaction map is driven by the attributes, not by the kind of type that
///     carries them.
///     <para>
///         A map emitted for message types only lets a mutation input with a <c>[NotLogged]</c>
///         member go out whole. Enumerating kinds instead — message, then action, then entity — would
///         move the gap to whatever kind came next rather than close it. And <c>[PersonalData]</c>
///         belongs in the map too: wired end to end for erasure and the Article 30 register, it also
///         has to keep the value out of a log.
///     </para>
/// </summary>
public class RedactionMapTests
{
    private const string Stubs = """
        namespace Pragmatic
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class NotLoggedAttribute : System.Attribute { }
        }
        namespace Pragmatic.Privacy
        {
            public enum DataCategory { Identity = 0, Contact = 1 }
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class PersonalDataAttribute(DataCategory category) : System.Attribute
            {
                public DataCategory Category { get; } = category;
            }
        }
        namespace Pragmatic.Serialization
        {
            public enum RedactionReason { NotLogged = 0, PersonalData = 1 }
            public readonly record struct RedactedMember(string Name, RedactionReason Reason, string? Category = null);
            public interface IRedactionMap
            {
                bool TryGetRedactedMembers(System.Type type, out System.Collections.Generic.IReadOnlyList<RedactedMember> members);
            }
        }
        namespace Microsoft.Extensions.DependencyInjection.Extensions
        {
            public static class ServiceCollectionDescriptorExtensions { }
        }
        namespace System.Text.Json.Serialization
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class JsonPropertyNameAttribute(string name) : System.Attribute
            {
                public string Name { get; } = name;
            }
        }
        """;

    private static string Map(string source)
        => GetGenerated(source, "RedactionMap");

    private static string GetGenerated(string source, string hint)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + source, []);
        return GeneratorTestHelper.GetGeneratedSource(result, hint) ?? "";
    }

    /// <summary>
    ///     A mutation, which the old map never covered. The type is not a message and does not need
    ///     to be — the attribute is the trigger.
    /// </summary>
    [Fact]
    public void AMarkedMemberOnAnyType_IsInTheMap()
    {
        var map = Map("""
            namespace Sample.Billing
            {
                public sealed record ChangeSupplierRate
                {
                    public required System.Guid Id { get; init; }
                    [Pragmatic.NotLogged]
                    public required string NegotiatedRate { get; init; }
                }
            }
            """);

        map.Should().Contain("ChangeSupplierRate").And.Contain("NegotiatedRate")
            .And.Contain("RedactionReason.NotLogged");
    }

    /// <summary>What was not marked must not be in the map, or the test above passes for free.</summary>
    [Fact]
    public void AnUnmarkedMemberIsNotInTheMap()
    {
        var map = Map("""
            namespace Sample.Billing
            {
                public sealed record ChangeSupplierRate
                {
                    public required System.Guid Id { get; init; }
                    [Pragmatic.NotLogged]
                    public required string NegotiatedRate { get; init; }
                }
            }
            """);

        map.Should().NotContain("\"Id\"");
    }

    [Fact]
    public void APersonalDataMember_IsInTheMapWithItsCategory()
    {
        var map = Map("""
            namespace Sample.Crm
            {
                public sealed class Customer
                {
                    [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """);

        map.Should().Contain("Email").And.Contain("RedactionReason.PersonalData").And.Contain("Contact");
    }

    /// <summary>
    ///     The serialized name, so redaction matches the payload rather than the CLR member.
    /// </summary>
    [Fact]
    public void JsonPropertyNameWins()
    {
        var map = Map("""
            namespace Sample.Billing
            {
                public sealed class Payload
                {
                    [Pragmatic.NotLogged]
                    [System.Text.Json.Serialization.JsonPropertyName("rate")]
                    public string NegotiatedRate { get; set; } = "";
                }
            }
            """);

        map.Should().Contain("\"rate\"").And.NotContain("\"NegotiatedRate\"");
    }

    /// <summary>
    ///     Emitted even with nothing declared, so that an absent map means one thing only: the
    ///     generator did not run. Same rule as the permission registry.
    /// </summary>
    [Fact]
    public void AnAssemblyThatDeclaresNothing_StillGetsAMap()
    {
        var map = Map("""
            namespace Sample.Plain
            {
                public sealed class Nothing { public string Value { get; set; } = ""; }
            }
            """);

        map.Should().Contain("GeneratedRedactionMap").And.Contain("members = [];");
    }

    [Fact]
    public void TheMapIsRegisteredWithTryAddEnumerable_SoAnApplicationsOwnComposes()
    {
        var registration = GetGenerated("""
            namespace Sample.Plain { public sealed class Nothing { } }
            """, "Redaction.Registration");

        registration.Should().Contain("TryAddEnumerable").And.Contain("GeneratedRedactionMap");
    }

    /// <summary>
    ///     A positional record. ForAttributeWithMetadataName does not report
    ///     <c>[property: NotLogged]</c> on a primary-constructor parameter — measured, after three
    ///     failed attempts to make it — so the feature scans record symbols as well. The
    ///     implementation this replaced did the same, and said so in a test summary I read without
    ///     understanding why.
    /// </summary>
    [Fact]
    public void APositionalRecordParameterIsCovered()
    {
        var map = Map("""
            namespace Sample.Auth
            {
                public sealed record LoginAttempted(string Email, [property: Pragmatic.NotLogged] string Password);
            }
            """);

        map.Should().Contain("Password");
    }

    // =========================================================================
    // What a type holds, not only what it declares
    // =========================================================================

    private const string AnEntityOwningAnIdentity = """
        namespace Sample.People
        {
            public sealed class Identity
            {
                [Pragmatic.NotLogged]
                public string PasswordHash { get; set; } = "";
                public string? Email { get; set; }
            }

            public sealed class Employee
            {
                public string FullName { get; set; } = "";
                public Identity Credentials { get; set; } = new();
            }
        }
        """;

    /// <summary>
    ///     A member classified on an owned type is in the outer type's map, as a path.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Employee</c> declares nothing of its own here, which is the case the defect was found
    ///     through: the map was built from the attribute's containing type alone, so what an entity
    ///     <b>held</b> was invisible — including the framework's own identity record, with the
    ///     password hash, two bearer tokens and the security stamp on it.
    /// </remarks>
    [Fact]
    public void AMemberClassifiedOnAnOwnedType_IsInTheOuterTypesMapAsAPath()
    {
        Map(AnEntityOwningAnIdentity).Should().Contain("Credentials.PasswordHash");
    }

    /// <summary>The control: what the owned type does not classify is not in the map either.</summary>
    /// <remarks>
    ///     Without it, "the nested member is covered" is satisfied by a walk that adds every member it
    ///     passes — which would mask a whole graph and leave a log entry saying nothing.
    /// </remarks>
    [Fact]
    public void AnUnclassifiedMemberOfAnOwnedType_IsNotInTheMap()
    {
        Map(AnEntityOwningAnIdentity).Should().NotContain("Credentials.Email");
    }

    /// <summary>A classified member inside a collection is a path over its elements.</summary>
    [Fact]
    public void AClassifiedMemberInsideACollection_IsAPathOverTheElements()
    {
        var map = Map("""
            namespace Sample.Teams
            {
                public sealed class Member
                {
                    [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }

                public sealed class Team
                {
                    public System.Collections.Generic.IReadOnlyList<Member> Members { get; set; } = [];
                }
            }
            """);

        map.Should().Contain("Members[].Email");
    }

    /// <summary>
    ///     A type that holds itself does not hang the generator.
    /// </summary>
    /// <remarks>
    ///     The cycle guard is per branch, so the walk ends where it would repeat. Asserting the map is
    ///     produced at all is the assertion: a generator that looped would never get here.
    /// </remarks>
    [Fact]
    public void ATypeThatHoldsItself_TerminatesAndStillProducesTheMap()
    {
        var map = Map("""
            namespace Sample.Org
            {
                public sealed class Person
                {
                    [Pragmatic.NotLogged]
                    public string Secret { get; set; } = "";
                    public Person? Manager { get; set; }
                }
            }
            """);

        map.Should().Contain("Manager.Secret", "one level down is still collected")
            .And.NotContain("Manager.Manager.Manager.Manager", "and the branch ends where it repeats");
    }
}
