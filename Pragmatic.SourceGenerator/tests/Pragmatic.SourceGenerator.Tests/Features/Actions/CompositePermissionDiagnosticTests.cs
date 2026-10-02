using Pragmatic.Actions.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     PRAG0440 — a composite that anyone can reach.
/// </summary>
/// <remarks>
///     <para>
///         A <c>[CompositeAction]</c> with an <c>[Endpoint]</c> is the door to its steps, and the door
///         has to say who may come through it. By default each step keeps its own permission check;
///         only a composite that declares <c>[AbsorbsChildPermissions]</c> runs them as internal calls,
///         and then the composite's own declaration is all there is. This diagnostic is what makes a
///         composite declare one.
///     </para>
///     <para>
///         An absorbing composite with no permission of its own lets an authenticated caller holding no
///         permissions at all receive <c>204</c> and write the rows, where each step's own endpoint would
///         answer <c>403</c>. That is why this is an error rather than a warning.
///     </para>
/// </remarks>
public class CompositePermissionDiagnosticTests
{
    [Fact]
    public void PRAG0440_ExposedCompositeWithNoPermission_IsReported()
    {
        var result = Run("""
            [DomainAction]
            [CompositeAction]
            [BelongsTo<ThingBoundary>]
            [Endpoint(HttpVerb.Post, "api/things/pairs")]
            public partial class CreatePairAction : VoidDomainAction
            {
                public required GuardedMutation First { get; init; }
                public required GuardedMutation Second { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0440").Should().BeTrue(
            "the steps require a permission and the composite, which is the door to them, declares none");
    }

    /// <summary>
    ///     The message is read by a consumer, so it must not say "a composite suppresses its steps'
    ///     permission checks", which is true only under <c>[AbsorbsChildPermissions]</c>. A diagnostic whose reason contradicts what the framework
    ///     does teaches the wrong model precisely where the reader is paying attention.
    /// </summary>
    [Fact]
    public void PRAG0440_TheMessage_DescribesTheDeclaredAbsorption_NotASuppressionByConstruction()
    {
        var result = Run("""
            [DomainAction]
            [CompositeAction]
            [BelongsTo<ThingBoundary>]
            [Endpoint(HttpVerb.Post, "api/things/pairs")]
            public partial class CreatePairAction : VoidDomainAction
            {
                public required GuardedMutation First { get; init; }
                public required GuardedMutation Second { get; init; }
            }
            """);

        var message = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0440").Single().GetMessage();

        message.Should().Contain("[AbsorbsChildPermissions]",
            "the one thing that switches the steps' own checks off is the declaration, and the message names it");
        message.Should().NotContain("suppresses",
            "the steps' checks are not suppressed by construction any more");
    }

    [Fact]
    public void PRAG0440_StaysSilentWhenTheCompositeDeclaresThePermission()
    {
        var result = Run("""
            [DomainAction]
            [CompositeAction]
            [BelongsTo<ThingBoundary>]
            [Endpoint(HttpVerb.Post, "api/things/pairs")]
            [RequirePermission("things.create")]
            public partial class CreatePairAction : VoidDomainAction
            {
                public required GuardedMutation First { get; init; }
                public required GuardedMutation Second { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0440").Should().BeFalse();
    }

    /// <remarks>
    ///     The escape hatch has to exist and has to be explicit: a composite that is meant to be public
    ///     says so, rather than being indistinguishable from one that forgot.
    /// </remarks>
    [Fact]
    public void PRAG0440_StaysSilentOnAnExplicitlyAnonymousComposite()
    {
        var result = Run("""
            [DomainAction]
            [CompositeAction]
            [BelongsTo<ThingBoundary>]
            [Endpoint(HttpVerb.Post, "api/things/pairs")]
            [AllowAnonymous]
            public partial class CreatePairAction : VoidDomainAction
            {
                public required GuardedMutation First { get; init; }
                public required GuardedMutation Second { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0440").Should().BeFalse();
    }

    /// <remarks>
    ///     Not exposed is not reachable. An internal composite is invoked by code that has already been
    ///     through a boundary of its own, and demanding a declaration there would be noise.
    /// </remarks>
    [Fact]
    public void PRAG0440_StaysSilentOnACompositeWithNoEndpoint()
    {
        var result = Run("""
            [DomainAction]
            [CompositeAction]
            [BelongsTo<ThingBoundary>]
            public partial class CreatePairAction : VoidDomainAction
            {
                public required GuardedMutation First { get; init; }
                public required GuardedMutation Second { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0440").Should().BeFalse();
    }

    /// <remarks>
    ///     Nothing to suppress, nothing to report. A composite of steps that require no permission is
    ///     not made safer by demanding one, and reporting it would train the reader to ignore the id.
    /// </remarks>
    [Fact]
    public void PRAG0440_StaysSilentWhenNoStepRequiresAPermission()
    {
        var result = Run("""
            [DomainAction]
            [CompositeAction]
            [BelongsTo<ThingBoundary>]
            [Endpoint(HttpVerb.Post, "api/things/pairs")]
            public partial class CreatePairAction : VoidDomainAction
            {
                public required OpenMutation First { get; init; }
                public required OpenMutation Second { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0440").Should().BeFalse();
    }

    private static SourceGenRunResult Run(string composite)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Composites;

            public sealed class ThingBoundary;

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Thing : IEntity
            {
                public string Name { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Create)]
            [RequirePermission("things.create")]
            public partial class GuardedMutation : Mutation<Thing>
            {
                public required string Name { get; init; }
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class OpenMutation : Mutation<Thing>
            {
                public required string Name { get; init; }
            }

            {{composite}}
            """,
            GeneratorTestHelper.FromType<CompositeActionAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Authorization.RequirePermissionAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
