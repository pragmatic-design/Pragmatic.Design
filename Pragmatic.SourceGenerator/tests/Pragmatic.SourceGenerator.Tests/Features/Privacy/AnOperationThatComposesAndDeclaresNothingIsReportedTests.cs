using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     An operation that reaches personal data through another operation and declares nothing is
///     missing from the Article 30 register, and now says so.
/// </summary>
/// <remarks>
///     <para>
///         The register derives an action's reach from the types of its dependencies. A boundary
///         interface names no entity, so <c>[ProcessesData&lt;T&gt;]</c> exists to state what cannot be
///         inferred — and nothing noticed the operation that states nothing. It left the register while
///         going on processing the data.
///     </para>
///     <para>
///         ⚠️ <b>Recognised by the dependency not resolving, and that is a read rather than a guess.</b>
///         A boundary interface is written by this generator in this same pass, so the module's
///         compilation does not contain it and the field's type is an error symbol carrying a name and
///         nothing else. In a build that otherwise succeeds, a dependency type that does not resolve
///         can only be generated code — anything else is a <c>CS0246</c> the author already sees. So the
///         check asks the symbol whether it resolved, which is a fact, instead of asking whether its
///         name looks like a boundary interface, which is a convention.
///     </para>
///     <para>
///         ⚠️ <b>Every case here leaves the interface undeclared, deliberately.</b> The removed first
///         attempt at this check was covered by four tests that declared it as a stub: they were green
///         while asserting behaviour that existed in no real module.
///     </para>
/// </remarks>
public class AnOperationThatComposesAndDeclaresNothingIsReportedTests
{
    private const string Subject = """
        namespace App
        {
            using Pragmatic.Privacy;

            [DataSubject("Email")]
            public class Customer
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                public string Email { get; set; } = "";
            }
        }
        """;

    /// <summary>The attributes as stubs; the real ones ship in Pragmatic.Privacy.Abstractions.</summary>
    private const string DeclarationStub = """
        namespace Pragmatic.Privacy
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class ProcessesDataAttribute<TEntity> : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ProcessesDataAttribute : System.Attribute { }
        }
        """;

    private static bool Reports(string action)
        => GeneratorTestHelper.HasDiagnostic(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
                PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs
                + PrivacyTestSources.OperationStubs + DeclarationStub + Subject + action,
                []),
            "PRAG2911");

    /// <summary>The setpoint: composes, declares nothing, and is told about it.</summary>
    [Fact]
    public void AnActionComposingAnUnresolvableInterfaceAndDeclaringNothing_IsReported()
    {
        Reports("""

            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers/import")]
                public partial class ImportCustomersAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private IAppInternalActions _app = null!;
                }
            }
            """)
            .Should().BeTrue("it reaches whatever those operations reach, and the register says nothing");
    }

    /// <summary>
    ///     ⚠️ The first control: an action that declares what it reaches is silent.
    /// </summary>
    /// <remarks>
    ///     Otherwise the check fires on the operations that did the right thing, which is the fastest
    ///     way to have a warning suppressed everywhere.
    /// </remarks>
    [Fact]
    public void AnActionThatDeclaresWhatItReaches_IsSilent()
    {
        Reports("""

            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Privacy.ProcessesData<Customer>]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers/import")]
                public partial class ImportCustomersAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private IAppInternalActions _app = null!;
                }
            }
            """)
            .Should().BeFalse();
    }

    /// <summary>
    ///     ⚠️ The second control: an action that composes and reaches nothing can say so.
    /// </summary>
    /// <remarks>
    ///     Without a way to say "reviewed, none", the only way to silence the check would be to declare
    ///     an entity the operation does not touch — which would put a lie in the register to quieten a
    ///     warning. The non-generic <c>[ProcessesData]</c> is that way, and it exists for this.
    /// </remarks>
    [Fact]
    public void AnActionThatDeclaresItReachesNothing_IsSilent()
    {
        Reports("""

            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Privacy.ProcessesData]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers/ping")]
                public partial class PingAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private IAppInternalActions _app = null!;
                }
            }
            """)
            .Should().BeFalse("'reviewed, none' is a statement, and the register needs it to be sayable");
    }

    /// <summary>
    ///     ⚠️ The third control: an action that composes nothing is silent.
    /// </summary>
    /// <remarks>
    ///     The check must not become "every action must carry an attribute". An action holding only a
    ///     repository already tells the register what it reaches, through the type it holds.
    /// </remarks>
    [Fact]
    public void AnActionWithNoCompositionAtAll_IsSilent()
    {
        Reports("""

            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "api/customers")]
                public partial class CreateCustomerAction : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private Pragmatic.Persistence.Repository.IRepository<Customer> _customers = null!;
                }
            }
            """)
            .Should().BeFalse("the type it holds already names what it reaches");
    }
}
