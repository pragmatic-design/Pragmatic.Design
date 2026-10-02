using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the DbContextRegistrationTemplate which generates DI registration extension methods.
/// </summary>
public class DbContextRegistrationTemplateTests
{
    [Fact]
    public void SingleBoundary_GeneratesRegistrationExtensions()
    {
        // Arrange
        var boundaries = ImmutableArray.Create(CreateSalesBoundary());

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("public static partial class");
        source.Should().Contain("AddSalesDbContext");
    }

    [Fact]
    public void SingleBoundary_RegistersDbContext()
    {
        // Arrange
        var boundaries = ImmutableArray.Create(CreateSalesBoundary());

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("SalesDbContext");
    }

    [Fact]
    public void MultipleBoundaries_RegistersAllDbContexts()
    {
        // Arrange
        var boundaries = ImmutableArray.Create(
            CreateSalesBoundary(),
            new BoundaryDbContextModel
            {
                Namespace = "MyApp.Inventory.Entities",
                ClassName = "InventoryDbContext",
                BoundaryName = "Inventory",
                BoundaryTypeName = "MyApp.Inventory.InventoryBoundary",
                IsMigrationContext = false,
                Entities = ImmutableArray.Create(
                    new DbContextEntityModel
                    {
                        FullTypeName = "MyApp.Inventory.Entities.Product",
                        TypeName = "Product",
                        DbSetName = "Products",
                        ConfigurationTypeName = "MyApp.Inventory.EntityConfigurations.ProductEntityConfiguration"
                    }
                )
            }
        );

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("SalesDbContext");
        source.Should().Contain("InventoryDbContext");
    }

    [Fact]
    public void GeneratesCorrectHintName()
    {
        // Arrange
        var boundaries = ImmutableArray.Create(CreateSalesBoundary());

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("DbContextRegistration");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void GeneratesExtensionMethodSignature()
    {
        // Arrange
        var boundaries = ImmutableArray.Create(CreateSalesBoundary());

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IServiceCollection services");
        source.Should().Contain("Action<DbContextOptionsBuilder>");
    }

    [Fact]
    public void GeneratesRequiredUsings()
    {
        // Arrange
        var boundaries = ImmutableArray.Create(CreateSalesBoundary());

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("using Microsoft.EntityFrameworkCore;");
        source.Should().Contain("using Microsoft.Extensions.DependencyInjection;");
    }

    [Fact]
    public void RealisticScenario_ContosoUniversity_GeneratesAllBoundaries()
    {
        // Arrange - All Contoso University boundaries
        var boundaries = ImmutableArray.Create(
            new BoundaryDbContextModel
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
                            "Contoso.University.Catalog.Entities.EntityConfigurations.CourseEntityConfiguration"
                    }
                )
            },
            new BoundaryDbContextModel
            {
                Namespace = "Contoso.University.Students.Entities",
                ClassName = "StudentsDbContext",
                BoundaryName = "Students",
                BoundaryTypeName = "Contoso.University.Students.StudentsBoundary",
                IsMigrationContext = false,
                Entities = ImmutableArray.Create(
                    new DbContextEntityModel
                    {
                        FullTypeName = "Contoso.University.Students.Entities.Student",
                        TypeName = "Student",
                        DbSetName = "Students",
                        ConfigurationTypeName =
                            "Contoso.University.Students.Entities.EntityConfigurations.StudentEntityConfiguration"
                    }
                )
            },
            new BoundaryDbContextModel
            {
                Namespace = "Contoso.University.Enrollment.Entities",
                ClassName = "EnrollmentDbContext",
                BoundaryName = "Enrollment",
                BoundaryTypeName = "Contoso.University.Enrollment.EnrollmentBoundary",
                IsMigrationContext = false,
                Entities = ImmutableArray.Create(
                    new DbContextEntityModel
                    {
                        FullTypeName = "Contoso.University.Enrollment.Entities.Enrollment",
                        TypeName = "Enrollment",
                        DbSetName = "Enrollments",
                        ConfigurationTypeName =
                            "Contoso.University.Enrollment.Entities.EntityConfigurations.EnrollmentEntityConfiguration"
                    }
                )
            }
        );

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "Contoso.University");
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("CatalogDbContext");
        source.Should().Contain("StudentsDbContext");
        source.Should().Contain("EnrollmentDbContext");
    }

    [Fact]
    public void EmptyBoundaries_ReturnsEmptySource()
    {
        // Arrange
        var boundaries = ImmutableArray<BoundaryDbContextModel>.Empty;

        // Act
        var template = new DbContextRegistrationTemplate(boundaries, "MyApp");
        var artifact = template.RenderOutput();

        // Assert - When Validate() fails, ToSourceText() returns empty SourceText (not null)
        artifact.Text.Should().BeEmpty();
    }

    private static BoundaryDbContextModel CreateSalesBoundary()
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
                    ConfigurationTypeName = "MyApp.Sales.EntityConfigurations.OrderEntityConfiguration"
                }
            )
        };
    }
}