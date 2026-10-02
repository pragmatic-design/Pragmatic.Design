using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[Join&lt;T&gt;(Via = …)]</c> is resolved against the entity, and goes through the
///     one channel the executor honours.
/// </summary>
/// <remarks>
///     <para>
///         Two defects of one declaration. <c>Via</c> was copied into the generated <c>Apply</c> unread,
///         so a name that is not a navigation was a <b>CS1061 inside a <c>.g.cs</c></b> — three of them,
///         on the <c>Include</c>, the filter and the specification — at a line of a file the author did
///         not write, for a string they wrote on an attribute. Every other string path in the query
///         surface (<c>MapTo</c>, <c>[EagerLoad]</c>) is resolved against the entity at compile time.
///     </para>
///     <para>
///         And the <c>Include</c> it emitted was a second, poorer copy of <c>IncludePaths</c>, which
///         every generated query already carries and which <c>EfCoreQueryExecutor.PrepareSource</c>
///         applies to the queryable <b>before</b> <c>Apply</c> runs. An <c>Include</c> inside
///         <c>Apply</c> is also dropped by EF Core as soon as the query projects, which is the normal
///         case — so on a projecting query it could not change an answer at all.
///     </para>
/// </remarks>
public class AJoinPathIsCheckedAgainstTheEntityTests
{
    [Fact]
    public void AViaThatNamesNoNavigation_IsReported_AndNoIncludeIsEmitted()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Nope")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0737").Should().BeTrue(
            "the name is on an attribute the author wrote, so it is checked where they can see it");

        // ⚠️ Asserted as "the path is not emitted" rather than "the compilation is clean": this
        // harness has no EF Core reference, so `Include` does not resolve here for a reason that has
        // nothing to do with the join. The CS1061 the issue measured — `'Invoice' does not contain a
        // definition for 'Guest'` — was on the real Showcase build, and what removes it is exactly
        // this: the name the author got wrong never reaches a generated file.
        GeneratorTestHelper.GetGeneratedSource(result, "JoinQuery.Query")
            .Should().NotContain("Nope",
                "an unresolved navigation used to be copied into Apply, and the author read a CS1061 "
                + "at a line of a file they did not write");
    }

    /// <summary>
    ///     The control: a <c>Via</c> that resolves is silent.
    /// </summary>
    /// <remarks>
    ///     Without it, "a wrong name is reported" is satisfied by reporting every name.
    /// </remarks>
    [Fact]
    public void AViaThatResolves_IsSilent()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Other")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0737").Should().BeFalse(
            "Thing.Other is a navigation of the entity the join is read on");
    }

    [Fact]
    public void AResolvedVia_IsAnIncludePath_AndApplyIncludesNothing()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Other")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "JoinQuery.Query");

        source.Should().Contain("IncludePaths => [\"Other\"",
            "the executor applies IncludePaths to the queryable before Apply runs, and that is the "
            + "channel it honours");
        source.Should().NotContain(".Include(",
            "a second Include inside Apply is a duplicate on a projecting query and dead weight on "
            + "every other");
    }

    /// <summary>
    ///     A dotted path is resolved segment by segment, on the type the previous one leads to.
    /// </summary>
    [Fact]
    public void ADottedViaIsResolvedSegmentBySegment()
    {
        var bad = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Other.Nope")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(bad, "PRAG0737").Should().BeTrue(
            "the second segment is read on Other, not on Thing");

        var good = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Other.Deeper")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(good, "PRAG0737").Should().BeFalse();
        GeneratorTestHelper.GetGeneratedSource(good, "JoinQuery.Query")
            .Should().Contain("\"Other.Deeper\"", "a dotted path reaches IncludePaths whole");
    }

    /// <summary>
    ///     The join's path and the response DTO's own requirements are one list, in the order an author
    ///     reads them.
    /// </summary>
    [Fact]
    public void AJoinPathSitsBesideTheDeclaredEagerLoads()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            [EagerLoad("Other")]
            [Join<Other>(Via = "Other.Deeper")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "JoinQuery.Query");

        source.Should().Contain("\"Other\"").And.Contain("\"Other.Deeper\"",
            "both declarations are the author's, and EF Core collapses a repeated Include");
    }

    private static SourceGenRunResult Run(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Endpoints.Attributes;

            namespace Joins;

            public sealed class ThingBoundary;

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Thing : IEntity
            {
                public string Name { get; private set; } = "";

                /// <summary>A navigation declared by hand: what Via is supposed to name.</summary>
                public Other Other { get; private set; } = null!;
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Other : IEntity
            {
                public string Note { get; private set; } = "";

                public Deep Deeper { get; private set; } = null!;
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Deep : IEntity
            {
                public string Detail { get; private set; } = "";
            }

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            // [EagerLoad] lives in Pragmatic.Actions, and without its reference the `using` fails, the
            // whole compilation is an error, and the generator emits an empty file — which reads as
            // "the feature produced nothing" rather than "the harness is short a reference".
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Mutation.EagerLoadAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
