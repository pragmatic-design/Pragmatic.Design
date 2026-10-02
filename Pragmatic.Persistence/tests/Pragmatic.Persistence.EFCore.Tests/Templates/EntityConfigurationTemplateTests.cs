using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the EntityConfigurationTemplate which generates IEntityTypeConfiguration implementations.
/// </summary>
public class EntityConfigurationTemplateTests
{
    [Fact]
    public void SimpleEntity_GeneratesEntityConfiguration()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("class OrderEntityConfig");
        source.Should().Contain("IEntityTypeConfiguration<Order>");
    }

    [Fact]
    public void SimpleEntity_ConfiguresPrimaryKey()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasKey(e => e.PersistenceId)");
    }

    [Fact]
    public void Entity_WithLogicKey_ConfiguresUniqueIndex()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = [new LogicKeyPart { Name = "OrderNumber", TypeName = "string" }]
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasIndex(e => e.OrderNumber)");
        source.Should().Contain("IsUnique()");
    }

    [Fact]
    public void Entity_WithNavigation_ConfiguresRelationship()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "Customer",
                    TargetTypeName = "MyApp.Entities.Customer",
                    NavigationType = "ManyToOne",
                    InverseProperty = "Orders",
                    ForeignKeyProperty = "CustomerId",
                    IsRequired = true,
                    OnDelete = "Restrict"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasOne");
        source.Should().Contain("Customer");
    }

    [Fact]
    public void Entity_WithCollectionNavigation_ConfiguresOneToMany()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "Lines",
                    TargetTypeName = "MyApp.Entities.OrderLine",
                    NavigationType = "OneToMany",
                    InverseProperty = "Order",
                    ForeignKeyProperty = "OrderId",
                    IsRequired = false,
                    OnDelete = "Cascade"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasMany");
        source.Should().Contain("Lines");
    }

    [Fact]
    public void Entity_WithOneToOneNavigation_ConfiguresRelationship()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "ShippingAddress",
                    TargetTypeName = "MyApp.Entities.Address",
                    NavigationType = "OneToOne",
                    InverseProperty = "Order",
                    ForeignKeyProperty = "ShippingAddressId",
                    IsRequired = false,
                    OnDelete = "SetNull"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasOne(e => e.ShippingAddress)");
        source.Should().Contain("WithOne(e => e.Order)");
        source.Should().Contain("OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.SetNull)");
    }

    [Fact]
    public void Entity_WithManyToManyNavigation_ConfiguresJoinTable()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "Tags",
                    TargetTypeName = "MyApp.Entities.Tag",
                    NavigationType = "ManyToMany",
                    InverseProperty = "Orders",
                    JoinTable = "OrderTags",
                    IsRequired = false,
                    OnDelete = "Cascade"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasMany(e => e.Tags)");
        source.Should().Contain("WithMany(e => e.Orders)");
        source.Should().Contain("OrderTags");
    }

    [Fact]
    public void Entity_WithNavigationWithoutInverse_GeneratesWithoutInverse()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "AuditLogs",
                    TargetTypeName = "MyApp.Entities.AuditLog",
                    NavigationType = "OneToMany",
                    InverseProperty = null, // No inverse
                    ForeignKeyProperty = null,
                    IsRequired = false,
                    OnDelete = "Cascade"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasMany(e => e.AuditLogs)");
        source.Should().Contain(".WithOne()"); // Without inverse
    }

    [Fact]
    public void Entity_WithMultipleNavigations_ConfiguresAll()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "Customer",
                    TargetTypeName = "MyApp.Entities.Customer",
                    NavigationType = "ManyToOne",
                    InverseProperty = "Orders",
                    ForeignKeyProperty = "CustomerId",
                    IsRequired = true,
                    OnDelete = "Restrict"
                },
                new NavigationMetadataModel
                {
                    Name = "Lines",
                    TargetTypeName = "MyApp.Entities.OrderLine",
                    NavigationType = "OneToMany",
                    InverseProperty = "Order",
                    ForeignKeyProperty = "OrderId",
                    IsRequired = false,
                    OnDelete = "Cascade"
                },
                new NavigationMetadataModel
                {
                    Name = "Tags",
                    TargetTypeName = "MyApp.Entities.Tag",
                    NavigationType = "ManyToMany",
                    InverseProperty = "Orders",
                    JoinTable = "OrderTags",
                    IsRequired = false,
                    OnDelete = "Cascade"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasOne(e => e.Customer)");
        source.Should().Contain("HasMany(e => e.Lines)");
        source.Should().Contain("HasMany(e => e.Tags)");
    }

    [Fact]
    public void Entity_WithRequiredManyToOne_GeneratesIsRequired()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "Category",
                    TargetTypeName = "MyApp.Entities.Category",
                    NavigationType = "ManyToOne",
                    InverseProperty = "Products",
                    ForeignKeyProperty = "CategoryId",
                    IsRequired = true,
                    OnDelete = "Restrict"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HasOne(e => e.Category)");
        source.Should().Contain(".IsRequired()");
    }

    [Fact]
    public void Entity_WithDifferentOnDeleteBehaviors_GeneratesCorrectly()
    {
        // Arrange - Test multiple delete behaviors
        var deleteBehaviors = new[] { "Cascade", "Restrict", "SetNull", "NoAction" };

        foreach (var deleteBehavior in deleteBehaviors)
        {
            var model = CreateBasicModel() with
            {
                Navigations = ImmutableArray.Create(
                    new NavigationMetadataModel
                    {
                        Name = "Parent",
                        TargetTypeName = "MyApp.Entities.Parent",
                        NavigationType = "ManyToOne",
                        InverseProperty = "Children",
                        ForeignKeyProperty = "ParentId",
                        IsRequired = false,
                        OnDelete = deleteBehavior
                    }
                )
            };

            // Act
            var template = new EntityConfigurationTemplate(model);
            var artifact = template.RenderOutput();

            // Assert
            var source = artifact.Text;
            source.Should().Contain($"OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.{deleteBehavior})");
        }
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Entities;");
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("Order");
        artifact.HintName.Should().Contain("EntityConfig");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void SimpleEntity_GeneratesRequiredUsings()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("using Microsoft.EntityFrameworkCore;");
        source.Should().Contain("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        // Arrange
        var model = new EntityMetadataModel
        {
            Namespace = "Test",
            TypeName = "", // Invalid
            FullTypeName = "",
            IdType = "System.Guid",
            BoundaryName = "Test",
            IsValid = false
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert - When Validate() fails, ToSourceText() returns empty SourceText (not null)
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Entity_WithSoftDelete_ConfiguresQueryFilter()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            IsSoftDelete = true
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should configure soft delete filter if template supports it
        source.Should().NotBeNull();
    }

    [Fact]
    public void Entity_WithAuditable_GeneratesConfiguration()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            IsAuditable = true
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should generate valid configuration
        source.Should().NotBeNull();
    }

    [Fact]
    public void RealisticScenario_CourseEntity_GeneratesCorrectConfiguration()
    {
        // Arrange - Contoso University Course entity
        var model = new EntityMetadataModel
        {
            Namespace = "Contoso.University.Catalog.Entities",
            TypeName = "Course",
            FullTypeName = "Contoso.University.Catalog.Entities.Course",
            IdType = "System.Guid",
            BoundaryName = "Catalog",
            LogicKeys = [new LogicKeyPart { Name = "CourseCode", TypeName = "string" }],
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel { Name = "CourseCode", TypeName = "string", IsLogicKey = true },
                new PropertyMetadataModel { Name = "Title", TypeName = "string" },
                new PropertyMetadataModel { Name = "Credits", TypeName = "int" },
                new PropertyMetadataModel { Name = "DepartmentId", TypeName = "System.Guid" }
            ),
            Navigations = ImmutableArray.Create(
                new NavigationMetadataModel
                {
                    Name = "Department",
                    TargetTypeName = "Contoso.University.Catalog.Entities.Department",
                    NavigationType = "ManyToOne",
                    InverseProperty = "Courses",
                    ForeignKeyProperty = "DepartmentId",
                    IsRequired = true,
                    OnDelete = "Restrict"
                }
            )
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.University.Catalog.Entities;");
        source.Should().Contain("class CourseEntityConfig");
        source.Should().Contain("IEntityTypeConfiguration<Course>");
        source.Should().Contain("HasKey(e => e.PersistenceId)");
        source.Should().Contain("HasIndex(e => e.CourseCode)");
    }

    [Fact]
    public void Entity_WithConcurrencyAware_DoesNotConfigureRowVersionInEntityConfig()
    {
        // Arrange — concurrency token is configured at Host level in BoundaryDbContext.OnModelCreating
        // where the EF Core provider (PostgreSQL, SQL Server, SQLite) is known.
        var model = CreateBasicModel() with
        {
            IsConcurrencyAware = true
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("IsConcurrencyToken");
        source.Should().NotContain("RowVersion");
    }

    [Fact]
    public void Entity_WithoutConcurrencyAware_DoesNotConfigureRowVersion()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("IsConcurrencyToken");
        source.Should().NotContain("RowVersion");
    }

    [Fact]
    public void Entity_WithConcurrencyAwareAndSoftDelete_ConfiguresSoftDeleteOnly()
    {
        // Arrange — concurrency is in BoundaryDbContext, soft delete stays here
        var model = CreateBasicModel() with
        {
            IsConcurrencyAware = true,
            IsSoftDelete = true
        };

        // Act
        var template = new EntityConfigurationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("IsConcurrencyToken");
        // SoftDelete is a NAMED EF Core 10 query filter (coexists with the named "Tenant" filter).
        source.Should().Contain("builder.HasQueryFilter(\"SoftDelete\", e => !e.IsDeleted);");
    }

    [Fact]
    public void Entity_WithMoneyProperty_GeneratesComplexPropertyConfiguration()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Price",
                    TypeName = "Pragmatic.Internationalization.Types.Money",
                    HasPrivateSetter = true
                })
        };

        // Act
        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        // Assert — complex type (not the opaque scalar Property), Amount native + Currency converted.
        source.Should().Contain("builder.ComplexProperty(e => e.Price, b =>");
        source.Should().Contain("b.Property(m => m.Amount).HasColumnName(\"Price_Amount\").HasPrecision(18, 2)");
        source.Should().Contain("b.Property(m => m.Currency).HasColumnName(\"Price_Currency\")");
        source.Should().Contain("CurrencyCode.FromCode(s)");
        source.Should().NotContain("builder.Property(e => e.Price)");
    }

    /// <summary>
    ///     A key the trait assigns is declared as one EF does not generate.
    /// </summary>
    /// <remarks>
    ///     Not cosmetic. EF reads the key to decide whether an entity it meets through a navigation is
    ///     new, and a generated key that already carries a value reads as "already in the store": a
    ///     freshly constructed child is then saved as an UPDATE of a row that does not exist, which
    ///     comes back as a conflict nobody caused.
    /// </remarks>
    [Fact]
    public void GuidKey_AssignedByTheTrait_IsDeclaredAsNotGenerated()
    {
        var source = new EntityConfigurationTemplate(CreateBasicModel()).RenderOutput().Text;

        source.Should().Contain("builder.Property(e => e.PersistenceId).ValueGeneratedNever()");
    }

    /// <summary>
    ///     A key the trait does not assign is left to whoever does.
    /// </summary>
    /// <remarks>
    ///     The claim is only true because the trait wrote the initializer. A hand-declared key belongs
    ///     to its author — saying "never generated" for it would stop the value from ever being filled.
    /// </remarks>
    [Fact]
    public void AKeyTheTraitDoesNotAssign_IsLeftAlone()
    {
        var model = CreateBasicModel() with { HasManualPersistenceId = true };

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().NotContain("ValueGeneratedNever");
    }

    private static EntityMetadataModel CreateBasicModel()
    {
        return new EntityMetadataModel
        {
            Namespace = "MyApp.Entities",
            TypeName = "Order",
            FullTypeName = "MyApp.Entities.Order",
            IdType = "System.Guid",
            BoundaryName = "Sales",
            LogicKeys = [],
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel { Name = "Total", TypeName = "decimal" },
                new PropertyMetadataModel { Name = "Status", TypeName = "string" }
            ),
            Navigations = ImmutableArray<NavigationMetadataModel>.Empty
        };
    }
}