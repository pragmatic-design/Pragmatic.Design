using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An operation that already holds the row can hand it to the mutation through the boundary,
///     instead of making it read the row again.
/// </summary>
/// <remarks>
///     <para>
///         <c>IMutationInvoker</c> has published a preloaded overload for exactly this, and the
///         boundary published no equivalent — so the one operation that needed it could not follow the
///         rule that an action never uses the invoker. The rule then had an exception nothing declared,
///         and the next author reads the file and copies the invoker form.
///     </para>
///     <para>
///         ⚠️ <b>On the internal interface only, and that is not a compromise.</b> The public interface
///         gets a remote implementation as well, and a tracked entity does not cross a process
///         boundary: publishing it there would force a remote method that can only throw, which is the
///         "generated code that says maybe" this repository forbids. The internal interface is the
///         intra-process composition seam — the one the caller in question uses — and there the entity
///         means something.
///     </para>
///     <para>
///         ⚠️ And not for a <c>Create</c> mutation. Creating is what that mode does, so supplying an
///         entity contradicts it; the invoker throws, and the boundary must not offer a shape whose
///         only outcome is that throw.
///     </para>
/// </remarks>
public class TheBoundaryTakesARowTheCallerAlreadyHoldsTests
{
    private const string Entity = "global::App.Accounts.Account";

    /// <summary>
    ///     The interfaces and the implementation are two files, and the mode chooses which.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Definition renders the interfaces, Local the class that implements them. Asserting on the
    ///     wrong one is how a signature assertion passes against a file that has no signatures in it.
    /// </remarks>
    private static string Render(
        MutationModeValue mode = MutationModeValue.Update,
        BoundaryOutputMode output = BoundaryOutputMode.Definition)
    {
        var mutation = new BoundaryMemberModel
        {
            TypeName = "CloseAccountMutation",
            FullTypeName = "global::App.Accounts.CloseAccountMutation",
            Namespace = "App.Accounts",
            IsMutation = true,
            IsVoid = false,
            ReturnTypeName = Entity,
            EntityFullTypeName = Entity,
            MutationCreates = mode == MutationModeValue.Create,
        };

        var boundary = new BoundaryModel
        {
            TypeName = "AccountsBoundary",
            FullTypeName = "global::App.Accounts.AccountsBoundary",
            Namespace = "App.Accounts",
            InterfaceName = "IAccountsActions",
            InternalInterfaceName = "IAccountsInternalActions",
            ImplementationName = "AccountsLocalActions",
            Accessibility = "public",
        };

        return new BoundaryInterfaceTemplate(
            boundary,
            ImmutableArray.Create(mutation),
            ImmutableArray<BoundaryMemberModel>.Empty,
            ImmutableArray<PackageActionRegistration>.Empty,
            output).RenderOutput().Text;
    }

    /// <summary>The setpoint: the internal interface publishes the preloaded shape.</summary>
    [Fact]
    public void TheInternalInterface_PublishesThePreloadedOverload()
    {
        Render().Should().Contain(
            $"CloseAccount(global::App.Accounts.CloseAccountMutation mutation, {Entity} entity,",
            "an operation holding the row hands it over rather than paying for a second SELECT");
    }

    /// <summary>And the implementation hands it to the invoker's own preloaded overload.</summary>
    [Fact]
    public void TheImplementation_PassesTheRowToTheInvoker()
    {
        Render(output: BoundaryOutputMode.Local).Should().Contain(".InvokeAsync(mutation, entity, ct)",
            "the boundary adds a shape, not a second pipeline");
    }

    /// <summary>
    ///     ⚠️ The first control: a Create mutation gains no such overload.
    /// </summary>
    /// <remarks>
    ///     Supplying an entity to a mutation whose mode is to create one contradicts it, and the
    ///     invoker throws. A boundary that offered the shape anyway would be publishing a call whose
    ///     only outcome is that throw.
    /// </remarks>
    [Fact]
    public void ACreateMutation_GainsNoPreloadedOverload()
    {
        Render(MutationModeValue.Create).Should().NotContain($"{Entity} entity,");
    }

    /// <summary>
    ///     ⚠️ The second control: the shape stays off the public interface and its remote twin.
    /// </summary>
    /// <remarks>
    ///     The public interface is implemented twice — locally and, for a boundary an application marks
    ///     remote, over HTTP. An entity handed to the second would have to be serialised, and what came
    ///     back would not be the row the caller is holding. Counting the occurrences is what says it is
    ///     published once: an assertion that it "appears" would pass with it on both.
    /// </remarks>
    [Fact]
    public void ThePublicInterface_DoesNotPublishIt()
    {
        var source = Render();
        var interfaceDeclarations = Occurrences(source, $"{Entity} entity, global::System.Threading.CancellationToken ct = default);");

        interfaceDeclarations.Should().Be(1,
            "declared on the internal interface and nowhere else — a tracked row cannot cross a wire");
    }

    /// <summary>
    ///     The same, for a boundary whose operations are grouped: the group gets an internal twin.
    /// </summary>
    /// <remarks>
    ///     A sub-boundary publishes one interface, and the remote implementation implements it — so the
    ///     preloaded shape cannot go there either. The twin is what the root's internal interface hands
    ///     back for that group, and nothing remote implements it.
    /// </remarks>
    private static string RenderGrouped(MutationModeValue mode = MutationModeValue.Update)
    {
        var mutation = new BoundaryMemberModel
        {
            TypeName = "CloseAccountMutation",
            FullTypeName = "global::App.Accounts.CloseAccountMutation",
            Namespace = "App.Accounts.Ledgers",
            IsMutation = true,
            IsVoid = false,
            ReturnTypeName = Entity,
            EntityFullTypeName = Entity,
            MutationCreates = mode == MutationModeValue.Create,
        };

        var sub = new SubBoundaryModel
        {
            Name = "Ledgers",
            FullPath = "Ledgers",
            InterfaceName = "IAccountsLedgersActions",
            ImplementationName = "AccountsLedgersLocalActions",
            PropertyName = "Ledgers",
            PublicMembers = ImmutableArray.Create(mutation),
        };

        var boundary = new BoundaryModel
        {
            TypeName = "AccountsBoundary",
            FullTypeName = "global::App.Accounts.AccountsBoundary",
            Namespace = "App.Accounts",
            InterfaceName = "IAccountsActions",
            InternalInterfaceName = "IAccountsInternalActions",
            ImplementationName = "AccountsLocalActions",
            Accessibility = "public",
            SubBoundaries = ImmutableArray.Create(sub),
        };

        return new BoundaryInterfaceTemplate(
            boundary,
            ImmutableArray<BoundaryMemberModel>.Empty,
            ImmutableArray<BoundaryMemberModel>.Empty,
            ImmutableArray<PackageActionRegistration>.Empty,
            BoundaryOutputMode.Definition).RenderOutput().Text;
    }

    /// <summary>The grouped setpoint: the group's own internal interface carries the shape.</summary>
    [Fact]
    public void AGroupedBoundary_GivesTheGroupAnInternalTwin()
    {
        var source = RenderGrouped();

        source.Should().Contain("internal interface IAccountsLedgersInternalActions : IAccountsLedgersActions",
            "the group's public interface is implemented over HTTP too, so the shape needs a twin");
        source.Should().Contain(
            $"CloseAccount(global::App.Accounts.CloseAccountMutation mutation, {Entity} entity,",
            "and the twin is where the preloaded shape is declared");
    }

    /// <summary>
    ///     And the root's internal interface hands back the twin, so one path reaches both shapes.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>new</c>, because the public interface this one extends already declares the property
    ///     with the public type. Without it the caller reaching the boundary internally would still get
    ///     the public group, and the shape would exist with no way to call it.
    /// </remarks>
    [Fact]
    public void TheRootInternalInterface_RetypesTheGroupToItsTwin()
    {
        RenderGrouped().Should().Contain("new IAccountsLedgersInternalActions Ledgers { get; }",
            "an operation inside the module reaches the twin through the root");
    }

    /// <summary>
    ///     ⚠️ The third control: a group with nothing to add gets no twin.
    /// </summary>
    /// <remarks>
    ///     An empty interface and a property returning it would be surface with nothing behind it, in
    ///     every application that groups its operations. Create is the case that produces it.
    /// </remarks>
    [Fact]
    public void AGroupWithNothingToAdd_GetsNoTwin()
    {
        var source = RenderGrouped(MutationModeValue.Create);

        source.Should().NotContain("IAccountsLedgersInternalActions",
            "no qualifying member, no twin and no property retyped to it");
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var at = text.IndexOf(value, System.StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(value, at + value.Length, System.StringComparison.Ordinal);
        }

        return count;
    }
}
