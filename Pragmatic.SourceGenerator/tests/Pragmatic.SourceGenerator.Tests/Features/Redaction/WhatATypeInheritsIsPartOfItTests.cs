using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Redaction;

/// <summary>
///     A member a type inherits is a member it has, and the redaction map has to say so
///     against the type that is actually logged.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The walk enumerates <c>GetMembers()</c>, which returns what a type <b>declares</b> and
///         not what it inherits. Found through <c>Pragmatic.Identity</c>: <c>LocalIdentity</c>'s four
///         secrets are declared on it and were masked, while <c>ExternalIdentityKey</c> — a composed
///         <c>{issuer}|{subject}</c> key holding the sign-in address, declared on the base
///         <c>IdentityRecord</c> — was not.
///     </para>
///     <para>
///         The lookup is by the runtime type of what is being logged, so an entry filed under the base
///         answers nothing when the derived type is serialised.
///     </para>
/// </remarks>
public class WhatATypeInheritsIsPartOfItTests
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
        => GeneratorTestHelper.GetGeneratedSource(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + source, []),
            "RedactionMap") ?? "";

    /// <summary>The shape found in the framework: the secret is on the base, the type logged is derived.</summary>
    private const string ASecretOnTheBase = """

        namespace Sample.Identity
        {
            public abstract class IdentityRecord
            {
                [Pragmatic.NotLogged]
                public string ExternalIdentityKey { get; set; } = "";

                public bool IsActive { get; set; }
            }

            public sealed class LocalIdentity : IdentityRecord
            {
                [Pragmatic.NotLogged]
                public string PasswordHash { get; set; } = "";
            }
        }
        """;

    /// <summary>
    ///     ⚠️ Asserted on the <b>one array</b> that answers for the derived type, not on the file.
    /// </summary>
    /// <remarks>
    ///     The first version of this test asked whether the text contained both names and passed
    ///     while the map filed the inherited member under the <em>base</em> — where the lookup, which
    ///     is by the runtime type of the object being logged, never finds it. A map is a set of
    ///     answers to "what does this type hold", so the assertion has to be about one answer.
    /// </remarks>
    [Fact]
    public void AnInheritedMember_IsInTheMapOfTheTypeThatInheritsIt()
    {
        AnswerFor(Map(ASecretOnTheBase), "Sample.Identity.LocalIdentity")
            .Should().Contain("ExternalIdentityKey",
                "the answer for the derived type has to carry what it inherits: the lookup is by the "
                + "runtime type of what is logged, and an entry filed under the base answers nothing");
    }

    /// <summary>
    ///     The control: a member nobody marked stays out, inherited or not.
    /// </summary>
    /// <remarks>
    ///     Without it, "the inherited member is in the map" is satisfied by a map that lists every
    ///     member of every type — which masks the fields that explain what a log entry is about, and is
    ///     how a redactor stops being used at all.
    /// </remarks>
    [Fact]
    public void AnInheritedMemberNobodyMarked_StaysOutOfTheMap()
    {
        Map(ASecretOnTheBase).Should().NotContain("IsActive");
    }

    /// <summary>
    ///     The same member, reached one level down as something an entity <b>owns</b>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the Time off shape exactly: <c>Employee</c> owns a <c>LocalIdentity</c>, whose base
    ///     declares the composed key holding the sign-in address. The descent into owned types walks
    ///     <c>GetMembers()</c> on the owned type, which is what the top-level pass does not have to do —
    ///     so the two halves of one map can disagree about the same member.
    /// </remarks>
    [Fact]
    public void AnInheritedMemberOfAnOwnedRecord_IsInTheOwnersMap()
    {
        var map = Map("""

            namespace Sample.People
            {
                public abstract class IdentityRecord
                {
                    [Pragmatic.NotLogged]
                    public string ExternalIdentityKey { get; set; } = "";
                }

                public sealed class LocalIdentity : IdentityRecord
                {
                    [Pragmatic.NotLogged]
                    public string PasswordHash { get; set; } = "";
                }

                public sealed class Employee
                {
                    public LocalIdentity? Identity { get; set; }

                    [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)]
                    public string WorkEmail { get; set; } = "";
                }
            }
            """);

        AnswerFor(map, "Sample.People.Employee")
            .Should().Contain("Identity.ExternalIdentityKey",
                "the employee's own answer carries both what the owned record declares and what it "
                + "inherits — it logs both");
    }

    /// <summary>
    ///     A member the derived type hides with <c>new</c> appears once in the derived type's answer.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Per type, not per file.</b> The base gets an answer of its own and should: somebody may
    ///     log a base-typed value. What must not happen is the same name twice in one answer, where the
    ///     redactor's reason and category would depend on which entry came first.
    /// </remarks>
    [Fact]
    public void AMemberHiddenWithNew_AppearsOnceInTheDerivedTypesAnswer()
    {
        var map = Map("""

            namespace Sample.Identity
            {
                public abstract class Base
                {
                    [Pragmatic.NotLogged]
                    public string Token { get; set; } = "";
                }

                public sealed class Derived : Base
                {
                    [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)]
                    public new string Token { get; set; } = "";
                }
            }
            """);

        var answer = AnswerFor(map, "Sample.Identity.Derived");

        CountOf(answer, "\"Token\"").Should().Be(1, "one member, one entry");
        answer.Should().Contain("RedactionReason.PersonalData",
            "and it is the derived declaration — the one the compiler binds — that says what it is");
    }

    /// <summary>
    ///     The <c>Members</c> array the map hands back for <paramref name="typeFqn" /> — its answer to
    ///     "what does this type hold".
    /// </summary>
    /// <remarks>
    ///     Followed through the <c>typeof</c> switch rather than guessed from the order of the arrays:
    ///     which array belongs to which type is exactly what an inherited member gets wrong.
    /// </remarks>
    private static string AnswerFor(string map, string typeFqn)
    {
        var lines = map.Split('\n');
        var at = Array.FindIndex(lines, l => l.Contains($"typeof(global::{typeFqn})", StringComparison.Ordinal));

        if (at < 0)
            throw new InvalidOperationException($"The map answers nothing for '{typeFqn}'.");

        var assignment = lines.Skip(at).First(l => l.Contains("members = Members", StringComparison.Ordinal));
        var name = assignment.Split("members = ")[1].TrimEnd(';', '\r', ' ');

        return lines.First(l => l.Contains($"{name} = [", StringComparison.Ordinal));
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, System.StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal);
        }

        return count;
    }
}
