using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Setters for a derived entity have exactly one owner.
/// </summary>
/// <remarks>
///     <para>
///         Two paths generated them: <c>EntityCoreFeature.GenerateEntitySetters</c> for every declared
///         <c>[Entity]</c>, and <c>PersistenceFeature.GenerateDerivedTypeSetters</c> for the derived types
///         of an <c>[Inheritance]</c> base. Both emitted <c>ForType(Type, "Setters", Namespace)</c>, so a
///         hierarchy whose derived type carries <c>[Entity]</c> — the shape <c>InheritanceAttribute</c>'s
///         own documentation shows — produced the same hint twice. Roslyn answers a duplicate hint by
///         discarding <b>the whole generator's output</b> under CS8785, which is a warning: the build goes
///         green with every generated file missing.
///     </para>
///     <para>
///         The first case is therefore about the file count, not about the setters. If it ever reads zero
///         again, nothing else in this class is meaningful.
///     </para>
/// </remarks>
public class DerivedEntitySetterOwnershipTests
{
    private const string Stubs = """
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }

        namespace Pragmatic.Persistence.Entity
        {
            public enum InheritanceStrategy { Tph, Tpt, Tpc }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class InheritanceAttribute : System.Attribute
            {
                public InheritanceAttribute(InheritanceStrategy strategy) { Strategy = strategy; }
                public InheritanceStrategy Strategy { get; }
                public string DiscriminatorColumn { get; set; } = "Discriminator";
                public string? DiscriminatorValue { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }

            public interface IEntity { System.Guid PersistenceId { get; } }
        }
        """;

    /// <summary>The shape <c>InheritanceAttribute</c>'s XML doc shows: the derived type carries [Entity].</summary>
    private const string DerivedIsDeclaredEntity = """

        namespace MyApp.Billing
        {
            using Pragmatic.Persistence.Entity;

            [Entity]
            [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "PaymentType")]
            public partial class Payment
            {
                public decimal Amount { get; private set; }
            }

            [Entity]
            public partial class CreditCardPayment : Payment
            {
                public string CardNumber { get; private set; } = "";
            }
        }
        """;

    /// <summary>The shape the Showcase has: the derived type is a plain partial class, no [Entity].</summary>
    private const string DerivedIsPlainClass = """

        namespace MyApp.Billing
        {
            using Pragmatic.Persistence.Entity;

            [Entity]
            [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "PaymentType")]
            public partial class Payment
            {
                public decimal Amount { get; private set; }
            }

            public partial class CreditCardPayment : Payment
            {
                public string CardNumber { get; private set; } = "";
            }
        }
        """;

    private static IReadOnlyDictionary<string, string> Generated(string source)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + source));

    /// <summary>
    ///     The measurement. A duplicate hint aborts the generator, so the symptom is an empty output —
    ///     not a wrong file.
    /// </summary>
    [Fact]
    public void DerivedTypeDeclaredAsAnEntity_DoesNotAbortTheGenerator()
    {
        var files = Generated(DerivedIsDeclaredEntity);

        files.Should().NotBeEmpty(
            "a duplicate hint name makes AddSource throw, and Roslyn then drops every file this "
            + "generator produced, reporting only the CS8785 warning");

        files.Keys.Should().Contain("MyApp.Billing.Payment.Create.g.cs",
            "the base entity's other artifacts are the proof the run completed, not just the setters");
    }

    /// <summary>One setter file for the derived type, and one method inside it.</summary>
    [Fact]
    public void DerivedTypeDeclaredAsAnEntity_GetsExactlyOneSetterForItsOwnProperty()
    {
        var files = Generated(DerivedIsDeclaredEntity);

        files.Keys.Where(k => k.Contains("CreditCardPayment") && k.Contains("Setters"))
            .Should().ContainSingle("the derived type's setters have one owner");

        files.Values.Sum(CountSetCardNumber).Should().Be(1,
            "two files declaring SetCardNumber in the same partial class is CS0111");
    }

    /// <summary>
    ///     Change tracking belongs to the base. Re-declaring it on the derived hides the base members
    ///     (CS0108) and, worse, gives the object a second <c>_modifiedProperties</c>: a setter on the
    ///     derived would then record into a set that <c>ModifiedProperties</c> on the base never shows.
    /// </summary>
    [Fact]
    public void DerivedTypeDeclaredAsAnEntity_DoesNotRedeclareChangeTracking()
    {
        var files = Generated(DerivedIsDeclaredEntity);

        var derivedSetters = files.Single(kv =>
            kv.Key.Contains("CreditCardPayment") && kv.Key.Contains("Setters")).Value;

        derivedSetters.Should().NotContain("IChangeTracking");
        derivedSetters.Should().NotContain("_modifiedProperties");

        files["MyApp.Billing.Payment.Setters.g.cs"]
            .Should().Contain("_modifiedProperties",
                "the base still owns the change-tracking infrastructure");
    }

    /// <summary>
    ///     The other supported shape, which is the only one the Showcase has and therefore the only one
    ///     that was ever exercised. It must keep its setters after the ownership split.
    /// </summary>
    [Fact]
    public void DerivedTypeWithoutTheEntityAttribute_StillGetsItsSetters()
    {
        var files = Generated(DerivedIsPlainClass);

        files.Values.Sum(CountSetCardNumber).Should().Be(1,
            "a derived type discovered through the [Inheritance] base, rather than declared as an "
            + "entity of its own, is what the Showcase writes");
    }

    private static int CountSetCardNumber(string source)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf("void SetCardNumber(", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 1;
        }

        return count;
    }
}
