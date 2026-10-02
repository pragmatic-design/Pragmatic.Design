using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An owned type reached through a nullable navigation has nullable columns, as EF's model
///     has them: <c>OwnsOne</c> maps it as an optional dependent and writes NULL into every column when the
///     navigation is null. The schema took each column's nullability from the owned type alone, so
///     <c>Identity_PasswordHash</c> was created NOT NULL and an employee's account could not be removed.
/// </summary>
/// <remarks>
///     Two halves: the module says whether the navigation is required (its metadata, read by the host),
///     and the schema reads it when it flattens the owned type into the owner's table.
/// </remarks>
public class AnOptionalOwnedTypeHasNullableColumnsTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Composition.Attributes.ModuleAttribute>()
    ];

    [Fact]
    public void ANonNullableOwnedNavigation_IsDeclaredRequired()
    {
        OwnedNavigation("Credentials Identity { get; set; } = new();").Should().Contain("\"isRequired\": true");
    }

    /// <summary>The control of the module half: a nullable navigation is not declared required.</summary>
    [Fact]
    public void ANullableOwnedNavigation_IsNotDeclaredRequired()
    {
        OwnedNavigation("Credentials? Identity { get; set; }").Should().NotContain("\"isRequired\": true");
    }

    [Fact]
    public void AnOptionalOwnedType_HasEveryColumnNullable()
    {
        var columns = Columns(identityRequired: false);

        columns["Identity_PasswordHash"].Should().BeTrue("EF writes NULL into it when the identity is removed");
        columns["Identity_EmailVerified"].Should().BeTrue("a value type too: the row with no identity has no value for it");
        columns["Identity_LockoutEnd"].Should().BeTrue();
    }

    /// <summary>The control of the schema half: a required owned type keeps its own nullability.</summary>
    [Fact]
    public void ARequiredOwnedType_KeepsItsColumnsNullability()
    {
        var columns = Columns(identityRequired: true);

        columns["Identity_PasswordHash"].Should().BeFalse();
        columns["Identity_EmailVerified"].Should().BeFalse();
        columns["Identity_LockoutEnd"].Should().BeTrue();
    }

    /// <summary>The module's persistence metadata for a person whose identity is declared as given.</summary>
    private static string OwnedNavigation(string identity)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Pragmatic.Identity
            {
                public abstract class IdentityRecord { public string ExternalIdentityKey { get; set; } = ""; }
            }

            namespace Contoso.Staff
            {
                [Boundary]
                public partial class StaffBoundary;

                public sealed class Credentials : Pragmatic.Identity.IdentityRecord
                {
                    public string PasswordHash { get; set; } = "";
                }
            }

            namespace Contoso.Staff.Entities
            {
                [Entity]
                public partial class Person : IEntity
                {
                    public string FullName { get; private set; } = "";

                    public {{identity}}
                }
            }
            """, References);

        var files = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        files.Should().ContainKey("_Metadata.Persistence.g.cs");
        return files["_Metadata.Persistence.g.cs"];
    }

    /// <summary>The owner's columns, by name, and whether each is nullable.</summary>
    private static Dictionary<string, bool> Columns(bool identityRequired)
    {
        var context = new SchemaMetadataTransform.SchemaResolutionContext
        {
            OwnedTypeProperties =
            {
                ["Credentials"] =
                [
                    ("PasswordHash", "string", false, false),
                    ("EmailVerified", "bool", false, false),
                    ("LockoutEnd", "System.DateTimeOffset?", true, false)
                ]
            }
        };

        var person = new EntityMetadataModel
        {
            TypeName = "Person",
            FullTypeName = "Test.Person",
            Namespace = "Test",
            IdType = "System.Guid",
            Accessibility = "public",
            IsValid = true,
            IsFromReference = true,
            Navigations = ImmutableArray.Create(new NavigationMetadataModel
            {
                Name = "Identity",
                TargetTypeName = "Test.Credentials",
                NavigationType = "Owned",
                IsOwned = true,
                IsRequired = identityRequired
            })
        };

        return SchemaMetadataTransform
            .Transform([person], EfCoreProvider.PostgreSql, "TestDb", "Test", context)
            .Tables.AsImmutableArray()
            .Single(t => t.EntityTypeName == person.FullTypeName)
            .Columns.AsImmutableArray()
            .Where(c => c.Name.StartsWith("Identity_", StringComparison.Ordinal))
            .ToDictionary(c => c.Name, c => c.IsNullable);
    }
}
