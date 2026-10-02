using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     The whole generated write, pinned across both files that make it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Two files, deliberately. Actions emits the one-line override and
///         <c>WrittenNavigations</c> (the navigations the write touches), and Mapping emits the body. Pinning only
///         one of them would hide exactly the failure mode that matters here — a member that moves to
///         the other generator and then stops being emitted by either. The separation is the thing
///         under test, so both halves are in the snapshot.
///     </para>
///     <para>
///         <c>Contain</c> checks see one line at a time: a property that stopped being written would
///         not fail any of them. A snapshot fails on anything that moves, which is the point.
///     </para>
///     <para>
///         ⚠️ The single navigation carries an <c>if (this.X is not null)</c> guard, as the mapping
///         side does. Without it the call reaches <c>MapOneToOne</c>, which reads null as detach — so
///         an update that merely omitted an optional child would remove the link.
///     </para>
///     <para>
///         One fixture rather than several, so the shapes are pinned together: nullable and not,
///         public setter and generated <c>Set{Name}</c>, a member no symbol carries yet (<c>Id</c>,
///         from <c>[Entity]</c>), <c>[MapIgnore]</c>, a collection child and a single one.
///     </para>
/// </remarks>
public class MutationAutoMapSnapshotTests : ActionsGeneratorTestBase
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    private const string EveryShape = $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            // private set is what makes the entity generator emit SetReference; a public setter is
            // written directly and needs no wrapper.
            public string Reference { get; private set; } = "";
            public string Code { get; set; } = "";
            public string? Notes { get; set; }
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
            public Address? ShippingAddress { get; set; }
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class LineItem : IEntity
        {
            public string Description { get; set; } = "";
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class Address : IEntity
        {
            public string Street { get; set; } = "";
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteLineItemMutation : Mutation<LineItem>
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteAddressMutation : Mutation<Address>
        {
            public Guid Id { get; init; }
            public string Street { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }

            /// <summary>Nullable, and the entity's setter is not public.</summary>
            public string? Reference { get; init; }

            /// <summary>Non-nullable, public setter: assigned unconditionally.</summary>
            public string Code { get; init; } = "";

            /// <summary>Nullable, public setter.</summary>
            public string? Notes { get; init; }

            /// <summary>Read by MutationTransform, and the only mapping attribute that is.</summary>
            [MapIgnore]
            public string? Scratch { get; init; }

            /// <summary>A collection child, merged rather than assigned.</summary>
            public List<WriteLineItemMutation> Lines { get; init; } = new();

            /// <summary>A single child, through the same door.</summary>
            public WriteAddressMutation? ShippingAddress { get; init; }
        }
        """;

    /// <summary>The override Actions keeps, and the body Mapping writes, side by side.</summary>
    [Fact]
    public async Task TheWriteBody_IsMappings_AndActionsKeepsTheOverride()
    {
        var result = RunGeneratorForMutation(EveryShape);

        var theOverride = GetGeneratedSource(result, "UpdateOrderMutation.ApplyToEntity");
        var theBody = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");

        theOverride.Should().NotBeNull(
            "the fixture has to reach the auto-map for the snapshot to mean anything");
        theBody.Should().NotBeNull(
            "the override delegates to ApplyToLoaded: without this file it names nothing");

        var errorsHere = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath is { } path
                        && (path.Contains("ApplyToEntity") || path.Contains("UpdateOrderMutation.Mapping")))
            .Select(d => d.ToString())
            .ToList();

        errorsHere.Should().BeEmpty(string.Join(Environment.NewLine, errorsHere));

        await Verify($"""
            // ===== Actions: UpdateOrderMutation.ApplyToEntity.g.cs =====
            {theOverride}
            // ===== Mapping: UpdateOrderMutation.Mapping.g.cs =====
            {theBody}
            """);
    }

    private static SourceGenRunResult RunGeneratorForMutation(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);
    }
}
