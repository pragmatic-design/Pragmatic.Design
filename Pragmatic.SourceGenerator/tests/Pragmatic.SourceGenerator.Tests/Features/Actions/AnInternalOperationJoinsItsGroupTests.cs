using System.Collections.Immutable;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Composition.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An operation grouped by its namespace is reached through its group, whether it is public or
///     internal.
/// </summary>
/// <remarks>
///     <para>
///         A member marked internal that went flat onto the root's internal interface whatever its
///         folder said, while the public members beside it were grouped, would make the path a caller
///         writes depend on the operation's <em>visibility</em>, which nothing in the layout
///         announces: <c>knowledge.Candidates.Discard(...)</c> and <c>knowledge.Settle(...)</c> for two
///         operations sitting in the same directory.
///     </para>
///     <para>
///         ⚠️ The group's internal twin is what makes this possible. Without it there is nowhere on a
///         sub-boundary to put a member that must not leave the process, and flat on the root is the
///         only place left.
///     </para>
/// </remarks>
public class AnInternalOperationJoinsItsGroupTests
{
    private const string Entity = "global::App.Accounts.Account";

    private static BoundaryMemberModel Member(string typeName, bool isInternal, bool creates = false) => new()
    {
        TypeName = typeName,
        FullTypeName = $"global::App.Accounts.Ledgers.{typeName}",
        Namespace = "App.Accounts.Ledgers",
        IsMutation = true,
        IsVoid = false,
        IsInternal = isInternal,
        MutationCreates = creates,
        ReturnTypeName = Entity,
        EntityFullTypeName = Entity,
    };

    private static string Render(BoundaryOutputMode output = BoundaryOutputMode.Definition)
        => Render(output, withTwin: true, boundaryIsInternal: false);

    /// <summary>The same group with every member public, so <c>HasInternalTwin</c> is false.</summary>
    private static string RenderWithoutTwin(BoundaryOutputMode output)
        => Render(output, withTwin: false, boundaryIsInternal: false);

    /// <summary>The other branch of <c>AddLocal</c>: a boundary nobody outside the module injects.</summary>
    private static string RenderInternalBoundary(BoundaryOutputMode output)
        => Render(output, withTwin: true, boundaryIsInternal: true);

    private static string Render(BoundaryOutputMode output, bool withTwin, bool boundaryIsInternal)
    {
        var sub = new SubBoundaryModel
        {
            Name = "Ledgers",
            FullPath = "Ledgers",
            InterfaceName = "IAccountsLedgersActions",
            ImplementationName = "AccountsLedgersLocalActions",
            PropertyName = "Ledgers",
            // ⚠️ Without a twin the public member has to *create*. `HasInternalTwin` is two things —
            // an internal member, or a preloaded shape — and an ordinary update mutation supplies the
            // second on its own: removing the internal member is not enough to leave the group without
            // a twin, which is the first thing this case measured.
            PublicMembers = ImmutableArray.Create(
                Member("CloseAccountMutation", isInternal: false, creates: !withTwin)),
            InternalMembers = withTwin
                ? ImmutableArray.Create(Member("SettleAccountMutation", isInternal: true))
                : ImmutableArray<BoundaryMemberModel>.Empty,
        };

        var boundary = new BoundaryModel
        {
            TypeName = "AccountsBoundary",
            FullTypeName = "global::App.Accounts.AccountsBoundary",
            Namespace = "App.Accounts",
            InterfaceName = "IAccountsActions",
            InternalInterfaceName = "IAccountsInternalActions",
            ImplementationName = "AccountsLocalActions",
            Accessibility = boundaryIsInternal ? "internal" : "public",
            IsInternal = boundaryIsInternal,
            SubBoundaries = ImmutableArray.Create(sub),
        };

        return new BoundaryInterfaceTemplate(
            boundary,
            ImmutableArray<BoundaryMemberModel>.Empty,
            ImmutableArray<BoundaryMemberModel>.Empty,
            ImmutableArray<PackageActionRegistration>.Empty,
            output).RenderOutput().Text;
    }

    /// <summary>The setpoint: the group's internal twin declares it.</summary>
    [Fact]
    public void TheGroupsInternalTwin_DeclaresTheInternalOperation()
    {
        Render().Should().Contain(
            "SettleAccount(global::App.Accounts.Ledgers.SettleAccountMutation mutation, "
            + "global::System.Threading.CancellationToken ct = default);",
            "an operation is reached through its group whatever its visibility");
    }

    /// <summary>⚠️ The first control: it is not also on the root.</summary>
    /// <remarks>
    ///     Adding it to the twin without removing it from the root would leave two paths to one
    ///     operation, and "it is on the group now" would be satisfied by a duplicate.
    /// </remarks>
    [Fact]
    public void TheRootInternalInterface_NoLongerDeclaresIt()
    {
        var source = Render();
        var rootInterface = Between(source, "internal interface IAccountsInternalActions", "\n}");

        rootInterface.Should().NotContain("SettleAccount(",
            "one operation, one path — the flat declaration is what this replaces");
    }

    /// <summary>⚠️ The second control: it stays off the group's public interface.</summary>
    /// <remarks>
    ///     The public one is what another module injects and what a remote boundary implements over
    ///     HTTP. Moving an internal operation into the group must not publish it.
    /// </remarks>
    [Fact]
    public void TheGroupsPublicInterface_DoesNotDeclareIt()
    {
        var source = Render();
        var publicInterface = Between(source, "public interface IAccountsLedgersActions", "\n}");

        publicInterface.Should().NotContain("SettleAccount(");
        publicInterface.Should().Contain("CloseAccount(", "the public member is still there");
    }

    /// <summary>And the implementation behind the twin answers it.</summary>
    [Fact]
    public void TheGroupsImplementation_ImplementsIt()
    {
        var source = Render(BoundaryOutputMode.Local);

        source.Should().Contain("SettleAccount(", "the twin is an interface something has to satisfy");
    }

    /// <summary>
    ///     The group's internal twin is registered locally — so a caller with no principal can inject
    ///     it — and is still absent from the remote registration, which nothing implements.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Registering the twin looks like a second, unguarded door, and is not one.</b> On a
    ///         group the permission distinction is drawn by <em>instance</em> rather than by which
    ///         interface is injected — the public group interface resolves to the guarded twin, the
    ///         unguarded one is held by the root's internal implementation.
    ///     </para>
    ///     <para>
    ///         The twin is <c>internal</c>, so only code in the same assembly can inject it, and that
    ///         same code can already inject the registered, unguarded <c>I{Boundary}InternalActions</c>
    ///         and reach the very same instance through <c>.{Group}</c>. The door leads into a room the
    ///         caller is already standing in; what it adds is a shorter name, not an access. Leaving it
    ///         unregistered costs a diagnosis: a <c>[MessageHandler]</c> or a <c>[Job]</c> — the two
    ///         callers that arrive with no principal, and the two the generated documentation points at
    ///         the internal interface — cannot be constructed, so the message is nacked and dropped with
    ///         no row and no error.
    ///     </para>
    ///     <para>
    ///         Spec 7.25's corollary states the same rule.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheGroupsInternalTwin_IsRegisteredLocallyAndNotRemotely()
    {
        var remote = Render(BoundaryOutputMode.Remote);
        remote.Should().NotContain("IAccountsLedgersInternalActions",
            "nothing remote implements the twin, so the remote registration cannot name it either");
        remote.Should().Contain("public IAccountsLedgersActions Ledgers => this;",
            "the control: the remote file does render the group, as its public interface");

        // ⚠️ AddLocal is rendered into the Local file, not beside the interfaces: an assertion on the
        // Definition file alone passes whatever the registration says.
        var local = Render(BoundaryOutputMode.Local);

        local.Should().Contain(
            "services.AddScoped<IAccountsLedgersInternalActions>(sp => sp.GetRequiredService<AccountsLedgersLocalActions>());",
            "a caller with no principal injects the twin by name instead of going through the root");

        // The control: the group's PUBLIC interface still resolves to the GUARDED twin. Registering the
        // internal one is only safe while this holds — if both interfaces resolved to the unguarded
        // implementation, the twin's registration would open the door the guarded interface keeps shut.
        local.Should().Contain(
            "services.AddScoped<IAccountsLedgersActions>(sp => sp.GetRequiredService<AccountsLedgersLocalGuardedActions>());");
        Between(Render(), "internal interface IAccountsInternalActions", "\n}")
            .Should().Contain("new IAccountsLedgersInternalActions Ledgers { get; }",
                "the root's internal interface still reaches the twin: this adds a door, it replaces none");
    }

    /// <summary>
    ///     ⚠️ The registration resolves the <b>unguarded</b> implementation — the same object the root
    ///     hands out — and not a second one.
    /// </summary>
    /// <remarks>
    ///     This is the control that keeps the twin's registration from becoming a defect. Both twins
    ///     implement the group's public interface, so a registration that named the guarded one, or that
    ///     constructed its own, would make <c>Root.Ledgers</c> and the injected twin two different
    ///     objects with two different permission behaviours under one interface. The implementation is
    ///     registered <c>AddScoped</c> and the root takes it as a constructor parameter, so resolving
    ///     the same concrete type is what makes them one instance per scope.
    /// </remarks>
    [Fact]
    public void TheRegisteredTwin_IsTheSameInstanceTheRootHandsOut()
    {
        var local = Render(BoundaryOutputMode.Local);

        local.Should().NotContain("AddScoped<IAccountsLedgersInternalActions>(sp => sp.GetRequiredService<AccountsLedgersLocalGuardedActions>())",
            "the guarded twin does not implement the internal interface, and would ask the permission twice");
        local.Should().NotContain("AddScoped<IAccountsLedgersInternalActions, ",
            "a type registration constructs its own; the root's instance is the one that must answer");
        local.Should().Contain("services.AddScoped<AccountsLedgersLocalActions>();",
            "the concrete type both the root and the twin resolve to is scoped");
    }

    /// <summary>
    ///     ⚠️ <c>AddRemote</c> registers the boundary's <b>public</b> interface and nothing else — so
    ///     the root's internal interface is no more injectable in remote mode than the group's twin is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This measures the remote objection to registering the twin, and <b>refutes</b> it. The
    ///         objection: a module injecting the twin would compile and run locally and fail to start the
    ///         day its boundary goes remote, whereas <c>Root.{Group}.Operation(…)</c> would degrade
    ///         honestly. It does not. Reaching <c>.{Group}</c> means injecting
    ///         <c>I{Boundary}InternalActions</c>, and <c>AddRemote</c> does not register that either —
    ///         the only two registrations of an internal interface the generator emits are both in
    ///         <c>AddLocal</c>.
    ///     </para>
    ///     <para>
    ///         So the asymmetry is symmetric: registering the twin adds no remote hazard that the
    ///         supported path does not already have. Whether a boundary composed <c>Remote</c> should
    ///         refuse, warn, or say nothing when something in its process injects an internal interface
    ///         is a separate question, not answered here.
    ///     </para>
    /// </remarks>
    [Fact]
    public void RemoteMode_RegistersNeitherInternalInterface()
    {
        var remote = Render(BoundaryOutputMode.Remote);

        remote.Should().NotContain("AddScoped<IAccountsInternalActions>",
            "the root's internal interface is local-only too, which is what makes the twin no worse");
        remote.Should().NotContain("AddScoped<IAccountsLedgersInternalActions>");

        // The control: AddRemote does register something, so these absences are not an empty file.
        remote.Should().Contain("services.AddScoped<IAccountsActions>(",
            "the public interface is what a remote boundary answers");
    }

    /// <summary>
    ///     A group with no internal operation and no preloaded shape has no twin, so nothing is
    ///     registered for one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The control on the guard: <c>HasInternalTwin</c> is what decides, and a registration
    ///         emitted unconditionally would name an interface the definition file never declared —
    ///         which does not fail this suite, because the template renders one file at a time, but
    ///         fails the consumer's build.
    ///     </para>
    ///     <para>
    ///         ⚠️ Reaching this state needs more than dropping the internal member: an ordinary update
    ///         mutation has a <b>preloaded shape</b>, which is the twin's other reason to exist. The
    ///         group here creates, so there is nothing to hand it a row for. Written the obvious way,
    ///         this case was red against a generator that was right.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AGroupWithNoTwin_RegistersNothingForOne()
    {
        var local = RenderWithoutTwin(BoundaryOutputMode.Local);

        local.Should().NotContain("IAccountsLedgersInternalActions",
            "there is no twin to register when every member of the group is public");
        local.Should().Contain(
            "services.AddScoped<IAccountsLedgersActions>(sp => sp.GetRequiredService<AccountsLedgersLocalGuardedActions>());",
            "the control: the group is still registered");
    }

    /// <summary>
    ///     An <b>internal</b> boundary has no guarded twin at all, and its group's internal twin is
    ///     registered just the same.
    /// </summary>
    /// <remarks>
    ///     The two branches of <c>AddLocal</c> differ, and the twin belongs to both: an internal
    ///     boundary resolves the group's public interface to the unguarded implementation because there
    ///     is nobody outside the module to guard against. A registration written into the public branch
    ///     only would leave the same hole for every internal boundary — which is the shape a message
    ///     handler is most likely to be calling.
    /// </remarks>
    [Fact]
    public void AnInternalBoundary_AlsoRegistersTheTwin()
    {
        var local = RenderInternalBoundary(BoundaryOutputMode.Local);

        local.Should().Contain(
            "services.AddScoped<IAccountsLedgersInternalActions>(sp => sp.GetRequiredService<AccountsLedgersLocalActions>());");
        local.Should().NotContain("GuardedActions",
            "the control: an internal boundary generates no guarded twin, so this is the other branch");
    }

    /// <summary>
    ///     ⚠️ The same claim through the real generator, because the template alone cannot see it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cases above build the model by hand and hand it to the template. They passed while
    ///         the generated code still declared the operation <b>twice</b> — once on the group's twin
    ///         and once flat on the root — because the root's internal members are not taken from what
    ///         the grouping returned: they are re-derived from every matched member, in the caller. Two
    ///         producers of one list, and a test that supplies the list itself is blind to the second.
    ///     </para>
    ///     <para>
    ///         Found by building a consumer application, not by this suite. This case is what makes the
    ///         suite able to find it next time.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ThroughTheGenerator_TheOperationIsDeclaredOnce()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing
            {
                [Module]
                public sealed class BillingModule;

                [Entity]
                public partial class Invoice : IEntity
                {
                    public string Number { get; private set; } = "";
                }
            }

            namespace Contoso.Billing.Ledgers.Mutations
            {
                // No [Endpoint], so this one is internal to the boundary — and it sits in a
                // sub-namespace, so it belongs to the Ledgers group.
                [Mutation(Mode = MutationMode.Update)]
                public partial class SettleInvoiceMutation : Mutation<Contoso.Billing.Invoice>
                {
                    public required Guid Id { get; init; }
                }

                [Mutation(Mode = MutationMode.Update)]
                [Endpoint(HttpVerb.Post, "api/ledgers/close")]
                public partial class CloseInvoiceMutation : Mutation<Contoso.Billing.Invoice>
                {
                    public required Guid Id { get; init; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source,
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<BoundaryAttribute>(),
            GeneratorTestHelper.FromType<ModuleAttribute>(),
            GeneratorTestHelper.FromType<EndpointAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Mutation<>)),
        ]);

        // ⚠️ The result does NOT compile, and cannot: the reference set is the minimum this case needs,
        // so the generated registrations name Microsoft.Extensions.DependencyInjection and
        // Pragmatic.Authorization.Policy, which are not here. What is asserted below is which
        // declarations the generator wrote, and a missing reference does not add or remove one. An
        // assertion on the compilation would be asserting the reference list.
        var definition = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(f => f.Key.Contains("Definition")).Value;

        // ⚠️ The plain shape, not the method name. One operation legitimately has three overloads on
        // its interface — the mutation, the preloaded one, and the unwrapped parameters — so counting
        // the name would read a correct generator as a duplicate.
        Declarations(definition,
                "SettleInvoiceMutation mutation, global::System.Threading.CancellationToken ct = default);")
            .Should().Be(1, "one operation, one path — on the group's twin and nowhere else");

        Declarations(definition, "SettleInvoiceMutation mutation, global::Contoso.Billing.Invoice entity,")
            .Should().Be(1, "and its preloaded shape moved with it rather than being left behind");
    }

    /// <summary>
    ///     Counts declarations, not mentions: a signature line ends the statement and a doc line does
    ///     not.
    /// </summary>
    private static int Declarations(string source, string signature)
    {
        var count = 0;

        foreach (var line in source.Split('\n'))
            if (line.Contains(signature) && !line.Contains("see cref"))
                count++;

        return count;
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, System.StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, $"'{start}' must be in the generated output");

        var to = text.IndexOf(end, from, System.StringComparison.Ordinal);
        return to < 0 ? text.Substring(from) : text.Substring(from, to - from);
    }
}
