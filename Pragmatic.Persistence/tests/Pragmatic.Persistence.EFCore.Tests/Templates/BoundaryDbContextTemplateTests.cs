using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the BoundaryDbContextTemplate which generates per-boundary DbContext classes.
/// </summary>
public class BoundaryDbContextTemplateTests
{
    [Fact]
    public void SimpleBoundary_GeneratesDbContextClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("public partial class SalesDbContext");
        source.Should().Contain(": Microsoft.EntityFrameworkCore.DbContext");
    }

    [Fact]
    public void SimpleBoundary_GeneratesDbSetProperties()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("DbSet<MyApp.Sales.Entities.Order> Orders");
        source.Should().Contain("DbSet<MyApp.Sales.Entities.Customer> Customers");
    }

    [Fact]
    public void SimpleBoundary_GeneratesOnModelCreating()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("protected override void OnModelCreating(ModelBuilder modelBuilder)");
        source.Should().Contain("modelBuilder.ApplyConfiguration");
    }

    [Fact]
    public void SimpleBoundary_AppliesEntityConfigurations()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("OrderEntityConfig");
        source.Should().Contain("CustomerEntityConfig");
    }

    [Fact]
    public void BoundaryWithMultipleEntities_GeneratesAllDbSets()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Entities = ImmutableArray.Create(
                new DbContextEntityModel
                {
                    FullTypeName = "MyApp.Sales.Entities.Order",
                    TypeName = "Order",
                    DbSetName = "Orders",
                    ConfigurationTypeName = "MyApp.Sales.Entities.OrderEntityConfig"
                },
                new DbContextEntityModel
                {
                    FullTypeName = "MyApp.Sales.Entities.Customer",
                    TypeName = "Customer",
                    DbSetName = "Customers",
                    ConfigurationTypeName = "MyApp.Sales.Entities.CustomerEntityConfig"
                },
                new DbContextEntityModel
                {
                    FullTypeName = "MyApp.Sales.Entities.OrderLine",
                    TypeName = "OrderLine",
                    DbSetName = "OrderLines",
                    ConfigurationTypeName = "MyApp.Sales.Entities.OrderLineEntityConfig"
                }
            )
        };

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("DbSet<MyApp.Sales.Entities.Order>");
        source.Should().Contain("DbSet<MyApp.Sales.Entities.Customer>");
        source.Should().Contain("DbSet<MyApp.Sales.Entities.OrderLine>");
    }

    [Fact]
    public void MigrationContext_GeneratesCorrectClassName()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            ClassName = "MigrationDbContext",
            BoundaryName = "Migration",
            IsMigrationContext = true
        };

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("public partial class MigrationDbContext");
    }

    [Fact]
    public void SimpleBoundary_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Entities;");
    }

    [Fact]
    public void SimpleBoundary_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("Sales");
        artifact.HintName.Should().Contain("DbContext");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void SimpleBoundary_GeneratesRequiredUsings()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("using Microsoft.EntityFrameworkCore;");
    }

    [Fact]
    public void SimpleBoundary_GeneratesXmlDocumentation()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("/// <summary>");
        source.Should().Contain("DbContext for the Sales boundary");
    }

    [Fact]
    public void InvalidModel_EmptyClassName_ReturnsEmptySource()
    {
        // Arrange - IsValid is computed: !string.IsNullOrEmpty(ClassName) && Entities.Length > 0
        var model = CreateBasicModel() with
        {
            ClassName = "" // Makes IsValid = false
        };

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert - When Validate() fails, ToSourceText() returns empty SourceText (not null)
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void InvalidModel_EmptyEntities_ReturnsEmptySource()
    {
        // Arrange - IsValid is computed: !string.IsNullOrEmpty(ClassName) && Entities.Length > 0
        var model = CreateBasicModel() with
        {
            Entities = ImmutableArray<DbContextEntityModel>.Empty // Makes IsValid = false
        };

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert - When Validate() fails, ToSourceText() returns empty SourceText (not null)
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void RealisticScenario_CatalogBoundary_GeneratesCorrectDbContext()
    {
        // Arrange - Contoso University Catalog boundary
        var model = new BoundaryDbContextModel
        {
            Namespace = "Contoso.University.Catalog.Entities",
            ClassName = "CatalogDbContext",
            BoundaryName = "Catalog",
            BoundaryTypeName = "Contoso.University.Catalog.CatalogBoundary",
            IsMigrationContext = false,
            Entities = ImmutableArray.Create(
                new DbContextEntityModel
                {
                    FullTypeName = "Contoso.University.Catalog.Entities.Course",
                    TypeName = "Course",
                    DbSetName = "Courses",
                    ConfigurationTypeName =
                        "Contoso.University.Catalog.Entities.CourseEntityConfig"
                },
                new DbContextEntityModel
                {
                    FullTypeName = "Contoso.University.Catalog.Entities.Department",
                    TypeName = "Department",
                    DbSetName = "Departments",
                    ConfigurationTypeName =
                        "Contoso.University.Catalog.Entities.DepartmentEntityConfig"
                },
                new DbContextEntityModel
                {
                    FullTypeName = "Contoso.University.Catalog.Entities.Instructor",
                    TypeName = "Instructor",
                    DbSetName = "Instructors",
                    ConfigurationTypeName =
                        "Contoso.University.Catalog.Entities.InstructorEntityConfig"
                }
            )
        };

        // Act
        var template = new BoundaryDbContextTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.University.Catalog.Entities;");
        source.Should().Contain("public partial class CatalogDbContext");
        source.Should().Contain("DbSet<Contoso.University.Catalog.Entities.Course> Courses");
        source.Should().Contain("DbSet<Contoso.University.Catalog.Entities.Department> Departments");
        source.Should().Contain("DbSet<Contoso.University.Catalog.Entities.Instructor> Instructors");
    }

    /// <summary>
    ///     The subject registry's tables live in the database of the boundary that holds the
    ///     data subject, created by the same migration, as the audit trail's are for [Audited] entities.
    /// </summary>
    /// <remarks>
    ///     Nothing created them: PrivacyDbContext documents ApplyPrivacyConfigurations for consumers
    ///     keeping the tables beside their own data, and no host ever called it — the registry answered
    ///     every call with "relation Subjects does not exist".
    /// </remarks>
    [Fact]
    public void ABoundaryHoldingADataSubject_MapsTheSubjectRegistry()
    {
        var model = CreateBasicModel() with
        {
            Entities = ImmutableArray.Create(new DbContextEntityModel
            {
                FullTypeName = "MyApp.Sales.Entities.Customer",
                TypeName = "Customer",
                DbSetName = "Customers",
                ConfigurationTypeName = "MyApp.Sales.Entities.CustomerEntityConfig",
                IsDataSubject = true
            }),
            HasPrivacyEFCore = true
        };

        var source = new BoundaryDbContextTemplate(model).RenderOutput().Text;

        source.Should().Contain("global::Pragmatic.Privacy.EFCore.PrivacyDbContext.ApplyPrivacyConfigurations(modelBuilder);");
    }

    /// <summary>The control: without the EF Core registry package there is nothing to map.</summary>
    [Fact]
    public void ADataSubjectWithoutTheRegistryPackage_MapsNothing()
    {
        var model = CreateBasicModel() with
        {
            Entities = ImmutableArray.Create(new DbContextEntityModel
            {
                FullTypeName = "MyApp.Sales.Entities.Customer",
                TypeName = "Customer",
                DbSetName = "Customers",
                ConfigurationTypeName = "MyApp.Sales.Entities.CustomerEntityConfig",
                IsDataSubject = true
            })
        };

        new BoundaryDbContextTemplate(model).RenderOutput().Text.Should().NotContain("ApplyPrivacyConfigurations");
    }

    private static BoundaryDbContextModel CreateBasicModel()
    {
        return new BoundaryDbContextModel
        {
            Namespace = "MyApp.Sales.Entities",
            ClassName = "SalesDbContext",
            BoundaryName = "Sales",
            BoundaryTypeName = "MyApp.Sales.SalesBoundary",
            IsMigrationContext = false,
            Entities = ImmutableArray.Create(
                new DbContextEntityModel
                {
                    FullTypeName = "MyApp.Sales.Entities.Order",
                    TypeName = "Order",
                    DbSetName = "Orders",
                    ConfigurationTypeName = "MyApp.Sales.Entities.OrderEntityConfig"
                },
                new DbContextEntityModel
                {
                    FullTypeName = "MyApp.Sales.Entities.Customer",
                    TypeName = "Customer",
                    DbSetName = "Customers",
                    ConfigurationTypeName = "MyApp.Sales.Entities.CustomerEntityConfig"
                }
            )
        };
    }
}