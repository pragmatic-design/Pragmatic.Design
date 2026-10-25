using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     On the Sql engine an entity declares the members and filter classes it declares on EF Core.
/// </summary>
/// <remarks>
///     <para>
///         The Sql engine binds its query chains against these members, and the application names them, so a member EF Core's
///         entity has and the Sql engine's lacks is a chain that does not compile. The expected lists are the spike's
///         ShapeParity, taken from what this generator emitted on EF Core for the same entities.
///     </para>
///     <para>
///         <c>[ConcurrencyAware]</c> declares <c>RowVersion</c> on every engine (owner, 2026-10-25): the token the Sql engine reads
///         back and compares is the member EF Core maps as the provider's row version, no longer a shadow property beside it.
///     </para>
///     <para>
///         The bridge is a stub of the type the generator recognises it by; the Sql runtime is not referenced, so the Sql
///         feature itself stays off and only the shape templates speak.
///     </para>
/// </remarks>
public class TheShapeOnTheSqlEngineMatchesEfCoresTests
{
    private const string Entities = """
        using System;
        using Pragmatic.MultiTenancy;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Binding
        {
        [Entity] [SoftDelete] [HasOwner] [ConcurrencyAware] [Relation.ManyToOne<Property>]
        public partial class Reservation { public DateTimeOffset CheckIn { get; set; } }

        [Entity] [Auditable] [HasAccessScopes] [Relation.OneToMany<RoomType>]
        [Relation.ManyToMany<Amenity>.WithNavigation("Amenities", JoinTable = "PropertyAmenities")]
        public partial class Property : ITenantEntity { public string Name { get; set; } = ""; public string TenantId { get; set; } = ""; }

        [Entity] [SoftDelete(Cascade = true)]
        public partial class RoomType { public string Code { get; set; } = ""; public int Beds { get; private set; } }

        [Entity]
        public partial class Amenity { public string Name { get; set; } = ""; }

        [Entity] [TemporalRelation]
        public partial class StaffAssignment : ITemporalRelation { public string Person { get; set; } = ""; public DateTimeOffset ValidFrom { get; set; } public DateTimeOffset? ValidTo { get; set; } }
        }
        """;

    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private static readonly Dictionary<string, string[]> Expected = new()
    {
        ["Reservation"] =
        [
            "interface Pragmatic.Persistence.Entity.ISoftDelete", "interface Pragmatic.Persistence.Entity.IOwnershipAssignable",
            "PersistenceId: System.Guid get set", "Id: System.Guid get", "IsDeleted: bool get set", "DeletedAt: System.DateTimeOffset? get set",
            "DeletedBy: string? get set", "PropertyId: System.Guid get private set", "Property: Probe.Binding.Property get set",
            "OwnerId: string get private set", "SetOwnerId(string)", "SetPropertyId(System.Guid)",
            "Pragmatic.Persistence.Entity.IOwnershipAssignable.AssignOwner(string)",
            "RowVersion: ulong get private set",
        ],
        ["Property"] =
        [
            "interface Pragmatic.Persistence.Entity.IAuditable", "interface Pragmatic.Persistence.Entity.IScopedEntity",
            "PersistenceId: System.Guid get set", "Id: System.Guid get", "CreatedAt: System.DateTimeOffset get set", "CreatedBy: string? get set",
            "UpdatedAt: System.DateTimeOffset? get set", "UpdatedBy: string? get set", "AccessScopes: System.Collections.Generic.List<string> get private set",
            "GrantScope(string)", "RevokeScope(string)",
            "RoomTypes: System.Collections.Generic.ICollection<Probe.Binding.RoomType> get set", "Amenities: System.Collections.Generic.ICollection<Probe.Binding.Amenity> get set",
        ],
        ["RoomType"] =
        [
            "interface Pragmatic.Persistence.Entity.ISoftDelete",
            "PersistenceId: System.Guid get set", "Id: System.Guid get", "IsDeleted: bool get set", "DeletedAt: System.DateTimeOffset? get set",
            "DeletedBy: string? get set", "PropertyId: System.Guid get private set", "Property: Probe.Binding.Property get set", "SetPropertyId(System.Guid)",
            "interface Pragmatic.Persistence.Entity.IChangeTracking", "ModifiedProperties: System.Collections.Generic.IReadOnlySet<string> get",
            "CollectionsModified: System.Collections.Generic.IReadOnlySet<string> get", "ResetModifiedProperties()", "IsNew: bool get set", "SetBeds(int)",
        ],
        ["Amenity"] =
        [
            "PersistenceId: System.Guid get set", "Id: System.Guid get", "Properties: System.Collections.Generic.ICollection<Probe.Binding.Property> get set",
        ],
        ["StaffAssignment"] =
        [
            "PersistenceId: System.Guid get set", "Id: System.Guid get", "IncludeHistory(Pragmatic.Persistence.Query.Filters.IQueryFilterToggle)",
        ],
    };

    private static readonly Dictionary<string, string[]> ExpectedFilters = new()
    {
        ["Reservation"] =
        [
            "SoftDeleteFilter() : Pragmatic.Persistence.Query.Filters.IQueryFilter<Probe.Binding.Reservation> | 100 | Default",
            "OwnershipFilter(Pragmatic.Identity.ICurrentUser) : Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<Probe.Binding.Reservation> | \"reservation.view-all\" | 200 | Default",
        ],
        ["Property"] =
        [
            "TenantFilter(Pragmatic.MultiTenancy.ITenantContext) : Pragmatic.Persistence.Query.Filters.IQueryFilter<Probe.Binding.Property>, Pragmatic.Persistence.Query.Filters.ITenantFilter | 200 | All",
            "ScopedDataFilter(Pragmatic.Authorization.IUserScopeResolver, Pragmatic.Identity.ICurrentUser) : Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<Probe.Binding.Property>, Pragmatic.Persistence.Query.Filters.IScopeVisibilityFilter | \"property.view-all\" | 250 | Default",
        ],
        ["RoomType"] = ["SoftDeleteFilter() : Pragmatic.Persistence.Query.Filters.IQueryFilter<Probe.Binding.RoomType> | 100 | Default"],
        ["Amenity"] = [],
        ["StaffAssignment"] = ["TemporalFilter(System.TimeProvider?) : Pragmatic.Persistence.Query.Filters.IQueryFilter<Probe.Binding.StaffAssignment> | 200 | Default"],
    };

    [Theory]
    [InlineData("Reservation")]
    [InlineData("Property")]
    [InlineData("RoomType")]
    [InlineData("Amenity")]
    [InlineData("StaffAssignment")]
    public void OnTheSqlEngine_TheEntityDeclaresWhatItDeclaresOnEfCore(string entity)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Entities + SqlBridge, References);
        var type = result.OutputCompilation.GetTypeByMetadataName("Probe.Binding." + entity)!;
        // The factory (*.Create.g.cs) is not the shape the engines bind against: the expected lists leave it out.
        var generated = result.GeneratedTrees.Where(t => !t.FilePath.EndsWith(".Create.g.cs", StringComparison.Ordinal)).ToHashSet();

        var declared = Declared(type, generated).ToList();
        Expected[entity].Except(declared).Should().BeEmpty("the Sql engine's entity lacks what EF Core's declares");
        declared.Except(Expected[entity]).Should().BeEmpty("the Sql engine's entity declares what EF Core's does not");
        var filters = Filters(type).ToList();
        ExpectedFilters[entity].Except(filters).Should().BeEmpty("a filter class EF Core's entity has");
        filters.Except(ExpectedFilters[entity]).Should().BeEmpty("a filter class EF Core's entity does not have");
    }

    [Fact]
    public void OnTheSqlEngine_TheShapeCompiles()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Entities + SqlBridge, References);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty("the shape names nothing of EF Core");
    }

    /// <summary>On EF Core the token is the same member: the one the context maps as the provider's row version.</summary>
    [Fact]
    public void OnEfCore_TheRowVersionIsTheSameMember()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Entities,
            [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()]);
        var reservation = result.OutputCompilation.GetTypeByMetadataName("Probe.Binding.Reservation")!;

        reservation.GetMembers("RowVersion").OfType<IPropertySymbol>().Select(p => p.Type.ToDisplayString())
            .Should().Equal(["ulong"], "one form of the token, whichever engine persists the entity");
    }

    /// <summary>A RowVersion the entity declares itself is the entity's: the trait does not add a second one.</summary>
    [Fact]
    public void OnTheSqlEngine_ADeclaredRowVersionIsKept()
    {
        var source = Entities.Replace(
            "public partial class Reservation { public DateTimeOffset CheckIn { get; set; } }",
            "public partial class Reservation { public DateTimeOffset CheckIn { get; set; } public ulong RowVersion { get; internal set; } }");
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source + SqlBridge, References);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty("one RowVersion, the entity's");
        result.OutputCompilation.GetTypeByMetadataName("Probe.Binding.Reservation")!.GetMembers("RowVersion").Should().HaveCount(1);
    }

    /// <summary>The filter classes nested in the entity: constructor, interfaces and the values of their constant members.</summary>
    private static IEnumerable<string> Filters(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers().Where(t => t.Name.EndsWith("Filter", StringComparison.Ordinal)))
        {
            var constructor = nested.InstanceConstructors.Single();
            var values = new[] { "BypassPermission", "Priority", "Scope" }
                .Select(n => nested.GetMembers(n).OfType<IPropertySymbol>().SingleOrDefault())
                .Where(p => p is not null)
                .Select(p => ((PropertyDeclarationSyntax)p!.DeclaringSyntaxReferences.Single().GetSyntax()).ExpressionBody!.Expression.ToString()
                    .Replace("global::Pragmatic.Persistence.Query.Filters.FilterScope.", ""));
            yield return $"{nested.Name}({string.Join(", ", constructor.Parameters.Select(p => p.Type.ToDisplayString()))}) : "
                         + $"{string.Join(", ", nested.Interfaces.Select(i => i.ToDisplayString()))} | {string.Join(" | ", values)}";
        }
    }

    /// <summary>The members and interfaces the generator declared on the entity, as signatures.</summary>
    private static IEnumerable<string> Declared(INamedTypeSymbol type, HashSet<SyntaxTree> generated)
    {
        bool InGenerated(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Any(r => generated.Contains(r.SyntaxTree));
        var interfaces = type.DeclaringSyntaxReferences.Where(r => generated.Contains(r.SyntaxTree))
            .SelectMany(r => ((ClassDeclarationSyntax)r.GetSyntax()).BaseList?.Types.Select(t => t.Type.ToString().Replace("global::", "")) ?? []);
        foreach (var name in interfaces)
            yield return "interface " + name;
        foreach (var member in type.GetMembers().Where(InGenerated))
        {
            switch (member)
            {
                case IPropertySymbol property:
                    var accessors = property.SetMethod is null ? "get" : property.SetMethod.DeclaredAccessibility == Accessibility.Public ? "get set" : "get private set";
                    yield return $"{property.Name}: {property.Type.ToDisplayString()} {accessors}";
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation } method:
                    var name = method.ExplicitInterfaceImplementations.FirstOrDefault() is { } implemented ? $"{implemented.ContainingType.ToDisplayString()}.{implemented.Name}" : method.Name;
                    yield return $"{name}({string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString()))})";
                    break;
            }
        }
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. ByName("Pragmatic.Abstractions", "Pragmatic.Identity", "Pragmatic.Authorization", "Pragmatic.Specification",
            "System.Linq.Queryable", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
