using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     The auto-derivation posture reaches a query's <b>invoker</b>, which is the door every caller
///     uses — not only the route.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Landing only on <c>EndpointModel.Authorization</c>, the route's configuration, is not
///         enough: the derived name has to be written into the query model the invoker renders too,
///         as a declared <c>[RequirePermission]</c> is. Otherwise a read that declares nothing answers
///         <b>403 over HTTP and returns rows in process</b> through the boundary facade, which builds
///         that same invoker.
///     </para>
///     <para>
///         ⚠️ "A query has no invoker: the route is the only place it is protected" is false — a
///         query has an invoker, and the route is one door of two.
///     </para>
///     <para>
///         The route half of the same rule is <c>TheDerivedPermissionReachesAQueryTests</c> in
///         <c>Pragmatic.Endpoints.Tests</c>. It lives there because its fixture does not reference EF
///         Core, and without EF Core no invoker is emitted at all.
///     </para>
/// </remarks>
public class TheDerivedPermissionReachesTheQueryInvokerTests
{
    private const string OptIn = "[assembly: Pragmatic.Authorization.PragmaticAutoDerivePermissions]";

    /// <summary>The name the posture gives <c>SearchItemsQuery</c> in the Catalog boundary.</summary>
    private const string Derived = "\"catalog.items.search\"";

    private static string Source(string attributes, bool optIn) => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        {{(optIn ? OptIn : "")}}

        // ⚠️ Declared, not referenced: this suite does not reference Pragmatic.Endpoints, and the
        // opt-out is recognised by its metadata name. Without the type the attribute would not bind, the
        // reader would see nothing, and the opt-out control would pass while measuring an attribute
        // nobody read — which is the failure that control exists to catch.
        namespace Pragmatic.Endpoints.Attributes
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AllowAnonymousAttribute : Attribute;
        }

        namespace TestApp.Catalog
        {
            [Boundary]
            public partial class CatalogBoundary;

            [Entity]
            public partial class Item : IEntity
            {
                public string Name { get; private set; } = "";
            }

            [Query<Item>]
            {{attributes}}
            public partial class SearchItemsQuery
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Name { get; init; }
            }
        }
        """;

    private static string TheInvoker(string attributes, bool optIn = true)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source(attributes, optIn), References());

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "SearchItemsQuery.QueryInvoker");

        generated.Should().NotBeNull("the query's invoker is generated at all");

        return generated!;
    }

    [Fact]
    public void FlagOn_AQueryWithoutPermission_TheInvokerRequiresTheDerivedName()
    {
        TheInvoker("").Should().Contain(Derived,
            "the invoker is where the permission is enforced for every caller, and the boundary "
            + "facade reaches the read through it");
    }

    /// <summary>The control: an opt-out is an opt-out on both doors.</summary>
    /// <remarks>
    ///     Without it, "the invoker requires the derived name" would be satisfied by writing it into
    ///     every invoker — which would close the in-process reads of every module that opts in, and
    ///     would disagree with the route the same query publishes.
    /// </remarks>
    [Fact]
    public void FlagOn_AnAnonymousQuery_TheInvokerRequiresNothing()
    {
        TheInvoker("[AllowAnonymous]").Should().NotContain(Derived,
            "the two doors answer the same question, so they opt out together");
    }

    /// <summary>The second control: with the posture off, nothing is added.</summary>
    [Fact]
    public void FlagOff_AQueryWithoutPermission_TheInvokerRequiresNothing()
    {
        TheInvoker("", optIn: false).Should().NotContain(Derived);
    }

    /// <summary>The third: a declared permission was always reaching it, which is what hid the gap.</summary>
    [Fact]
    public void AQueryWithItsOwnPermission_TheInvokerKeepsIt()
    {
        var invoker = TheInvoker("[RequirePermission(\"catalog.items.audit\")]");

        invoker.Should().Contain("\"catalog.items.audit\"");
        invoker.Should().NotContain(Derived, "an author's choice is never swapped for a guess");
    }

    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
        GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        GeneratorTestHelper.FromType<Pragmatic.Authorization.PragmaticAutoDerivePermissionsAttribute>(),
        // Without it [Boundary] does not resolve, the boundary list is empty, and the derived name
        // loses its first segment — which is what the assertion would then be measuring.
        GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.BoundaryAttribute>(),
    ];
}
