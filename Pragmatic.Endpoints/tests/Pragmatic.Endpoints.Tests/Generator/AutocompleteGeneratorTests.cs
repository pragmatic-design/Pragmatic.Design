using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for [Autocomplete] source generation.
///     Verifies that entity properties with [Autocomplete] produce autocomplete endpoints.
/// </summary>
/// <remarks>
///     Note: These tests verify GENERATED TEXT output, not full compilation, because
///     the generated code references IReadRepository and EF Core methods not available in test context.
/// </remarks>
public class AutocompleteGeneratorTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Autocomplete_WithDefaultRoute_GeneratesEndpoint()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull("an autocomplete endpoint should be generated");

        // Verify class name
        generated.Should().Contain("class CustomerNameAutocompleteEndpoint");

        // Verify static class
        generated.Should().Contain("static");

        // Verify MapEndpoint method
        generated.Should().Contain("MapEndpoint(");

        // Verify default route
        generated.Should().Contain("MapGet(\"/customers/autocomplete/name\"");

        // Verify search parameter
        generated.Should().Contain("string? search");

        // Verify limit parameter
        generated.Should().Contain("int? limit");

        // Verify Contains filter
        generated.Should().Contain("e.Name.Contains(search)");

        // Verify Select with typed key and value
        generated.Should().Contain("e.Id, e.Name");
        generated.Should().Contain("AutocompleteItem<global::System.Guid>");
    }

    [Fact]
    public void Autocomplete_WithCustomRoute_UsesProvidedRoute()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete(Route = "api/customers/search-by-name")]
                             public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();
        generated.Should().Contain("api/customers/search-by-name");
    }

    [Fact]
    public void Autocomplete_WithCustomLimit_UsesProvidedLimit()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete(DefaultLimit = 25)]
                             public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();
        generated.Should().Contain("limit ?? 25");
    }

    [Fact]
    public void Autocomplete_EntityWithIntId_UsesIntKey()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             [Autocomplete] public string Title { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();
        generated.Should().Contain("class ProductTitleAutocompleteEndpoint");
        generated.Should().Contain("e.Id, e.Title");
        generated.Should().Contain("AutocompleteItem<int>");
    }

    [Fact]
    public void Autocomplete_EntityWithNoId_EmitsDiagnostic()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class BadEntity
                         {
                             public string Code { get; set; } = "";
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Should emit PRAG0515 diagnostic
        HasDiagnostic(result, "PRAG0515").Should().BeTrue(
            "entity without key property should produce PRAG0515 error");

        // Should NOT generate an endpoint
        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().BeNull("no endpoint should be generated without key property");
    }

    [Fact]
    public void Autocomplete_IncludedInRegistration()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Registration should include the autocomplete endpoint
        var registration = GetGeneratedSource(result, "Endpoints.Registration");
        registration.Should().NotBeNull("registration should be generated");
        registration.Should().Contain("CustomerNameAutocompleteEndpoint");
        registration.Should().Contain("MapEndpoint");
    }

    [Fact]
    public void Autocomplete_GeneratesCorrectHeader()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();

        // Header should include generator info
        generated.Should().Contain("// Pragmatic.SourceGenerator/Endpoints");
        generated.Should().Contain("[Autocomplete] on Customer.Name");
    }

    [Fact]
    public void Autocomplete_WithEndpointName_SetsWithName()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();
        generated.Should().Contain("WithName(\"CustomerNameAutocomplete\")");
    }

    [Fact]
    public void Autocomplete_ProducesMetadata_ForOpenAPI()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();
        generated.Should().Contain("ProducesResponseTypeMetadata(");
        generated.Should().Contain("AutocompleteItem<global::System.Guid>");
    }

    [Fact]
    public void Autocomplete_UsesRepository_NotDirectDbContext()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();

        // Must use IRepository (applies query filters: soft-delete, tenant, temporal)
        generated.Should().Contain("IRepository<");
        generated.Should().Contain("repository.Query()");

        // Must NOT use DbContext directly (bypasses all filters)
        generated.Should().NotContain("dbContext.Set<");
        generated.Should().NotContain("GetRequiredService<global::Microsoft.EntityFrameworkCore.DbContext>");
        generated.Should().NotContain("GetRequiredKeyedService<global::Microsoft.EntityFrameworkCore.DbContext>");
    }

    [Fact]
    public void Autocomplete_WithBoundary_UsesKeyedRepository()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace Pragmatic.Persistence.Entity
                     {
                         [System.AttributeUsage(System.AttributeTargets.Class)]
                         public class BelongsToAttribute<T> : System.Attribute { }
                     }
                     namespace TestApp
                     {
                         public class SalesBoundary { }
                     }
                     namespace TestApp.Entities
                     {
                         [Pragmatic.Persistence.Entity.BelongsTo<TestApp.SalesBoundary>]
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();

        // Must use DI for repository resolution
        generated.Should().Contain("GetRequiredService<global::Pragmatic.Persistence.Repository.IRepository");
        generated.Should().Contain("IRepository<");
    }

    [Fact]
    public void Autocomplete_MultipleProperties_GeneratesMultipleEndpoints()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                             [Autocomplete] public string Email { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Both endpoints should be generated
        var allSources = GetGeneratedSourcesAsDictionary(result);

        var autocompleteFiles = allSources
            .Where(kvp => kvp.Key.Contains("Autocomplete"))
            .ToList();

        autocompleteFiles.Should().HaveCount(2, "two autocomplete properties should generate two endpoints");

        // Registration should include both
        var registration = GetGeneratedSource(result, "Endpoints.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("CustomerNameAutocompleteEndpoint");
        registration.Should().Contain("CustomerEmailAutocompleteEndpoint");
    }

    // =========================================================================
    // [Autocomplete<TDto>] — DTO projection mode
    // =========================================================================

    [Fact]
    public void AutocompleteGeneric_WithDto_GeneratesFromEntityProjection()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Dtos
                     {
                         public partial record CustomerSearchResult
                         {
                             public System.Guid Id { get; init; }
                             public string Name { get; init; } = "";
                             public string Email { get; init; } = "";

                             public static CustomerSearchResult FromEntity(TestApp.Entities.Customer e)
                                 => new() { Id = e.Id, Name = e.Name, Email = e.Email };
                         }
                     }
                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete<TestApp.Dtos.CustomerSearchResult>]
                             public string Name { get; set; } = "";
                             public string Email { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull("a DTO-mode autocomplete endpoint should be generated");

        // Verify it uses FromEntity mapping
        generated.Should().Contain("FromEntity");

        // Verify it produces the DTO type, not AutocompleteItem
        generated.Should().Contain("CustomerSearchResult");
        generated.Should().NotContain("AutocompleteItem");

        // Verify search still filters on the property
        generated.Should().Contain("e.Name.Contains(search)");
    }

    [Fact]
    public void AutocompleteGeneric_WithDto_ProducesCorrectResponseType()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Dtos
                     {
                         public partial record ProductSearchResult
                         {
                             public static ProductSearchResult FromEntity(TestApp.Entities.Product e)
                                 => new();
                         }
                     }
                     namespace TestApp.Entities
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             [Autocomplete<TestApp.Dtos.ProductSearchResult>]
                             public string Title { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();

        // Produces should reference the DTO type
        generated.Should().Contain("ProducesResponseTypeMetadata(");
        generated.Should().Contain("ProductSearchResult");
    }

    [Fact]
    public void AutocompleteGeneric_IncludedInRegistration()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Dtos
                     {
                         public partial record CustomerSearchResult
                         {
                             public static CustomerSearchResult FromEntity(TestApp.Entities.Customer e)
                                 => new();
                         }
                     }
                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete<TestApp.Dtos.CustomerSearchResult>]
                             public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var registration = GetGeneratedSource(result, "Endpoints.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("CustomerNameAutocompleteEndpoint");
    }

    // The read permission must gate the route, not sit on it as WithMetadata(RequirePermissionAttribute):
    // that attribute is not IAuthorizeData and nothing reads it off the route metadata, so the endpoint
    // would declare a permission it does not require, and any authenticated caller could autocomplete
    // any exposed entity.
    [Fact]
    public void Autocomplete_ReadPermission_IsEnforcedAndNotJustDeclaredAsMetadata()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var generated = GetGeneratedSource(RunGenerator(source), "Autocomplete");

        generated.Should().NotBeNull();
        generated.Should().Contain("RequireAuthorization",
            "the permission has to gate the route, not merely describe it");
        generated.Should().Contain("customer.read");
        generated.Should().NotContain("WithMetadata(new global::Pragmatic.Authorization.RequirePermissionAttribute",
            "that metadata has no reader, so emitting it states a requirement nothing imposes");
    }

    /// <summary>
    ///     The module says nothing about who can call the route: whether anybody can hold the derived
    ///     permission is the <b>host's</b> question, and the host answers it (PRAG1692).
    /// </summary>
    /// <remarks>
    ///     ⚠️ A module without <c>Identity.AspNetCore</c> is not warned with <c>PRAG0519</c>. That
    ///     would ask the module something it cannot know, and the practical cost would be a
    ///     <c>ProjectReference</c> pinned to boundary libraries that need nothing from it — one warning
    ///     per <c>[Autocomplete]</c> without it.
    ///     <para>
    ///         The fact travels on the endpoint's <c>derivedPermission</c>, and
    ///         <c>AnAnonymousHostWithADerivedPermissionTests</c> is where the diagnostic is measured.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Autocomplete_WithoutIdentity_SaysNothingAboutTheHost()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Name { get; set; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0519").Should().BeFalse(
            "PRAG0519 is retired: the module cannot know whether the host authenticates");

        // And the route is still generated, gating on the permission it derives — what changed is who
        // gets told about it, not what is emitted.
        var generated = GetGeneratedSource(result, "Autocomplete");
        generated.Should().NotBeNull();
        generated.Should().Contain("customer.read");
    }

    /// <summary>
    ///     The entity name is kebab-cased, the same way the CRUD permissions are emitted.
    /// </summary>
    /// <remarks>
    ///     Every other test here names the entity <c>Customer</c>, and one word has no boundaries to
    ///     lose — which is why this went unnoticed. On <c>KnowledgeItem</c> the route enforced
    ///     <c>knowledge.knowledgeitem.read</c> while the permission a role can be granted is
    ///     <c>knowledge.knowledge-item.read</c>: a 403 that no grant could lift, found in a lab
    ///     scenario rather than here.
    /// </remarks>
    [Fact]
    public void Autocomplete_MultiWordEntity_EnforcesTheKebabPermissionCrudActuallyEmits()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;

                     namespace TestApp.Entities
                     {
                         public class KnowledgeItem
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Term { get; set; } = "";
                         }
                     }
                     """;

        var generated = GetGeneratedSource(RunGenerator(source), "Autocomplete");

        generated.Should().Contain("knowledge-item.read",
            "the enforced permission must be the one the CRUD generator emits");
        generated.Should().NotContain("knowledgeitem.read",
            "a flat lowercase spelling names a permission no role can ever hold");
    }

    /// <summary>
    ///     The same, on the branch a real application takes: an entity inside a boundary.
    /// </summary>
    /// <remarks>
    ///     Worth its own test rather than a second assertion. The two spellings were produced by two
    ///     branches of one method, so a test covering only the boundary-less one goes green while the
    ///     path every real entity takes stays broken — which is precisely what a first attempt at this
    ///     test did.
    /// </remarks>
    [Fact]
    public void Autocomplete_MultiWordEntityInABoundary_EnforcesTheKebabPermission()
    {
        var source = """
                     using Pragmatic.Endpoints.Attributes;
                     using Pragmatic.Persistence.Entity;

                     namespace TestApp
                     {
                         public partial class KnowledgeBoundary;
                     }

                     namespace TestApp.Entities
                     {
                         [BelongsTo<TestApp.KnowledgeBoundary>]
                         public class KnowledgeItem
                         {
                             public System.Guid Id { get; set; }
                             [Autocomplete] public string Term { get; set; } = "";
                         }
                     }
                     """;

        // With Persistence: [BelongsTo<T>] lives there, and without the reference the attribute does
        // not bind, the boundary reads as absent, and the test silently checks the other branch.
        var generated = GetGeneratedSource(RunGeneratorWithPersistence(source), "Autocomplete");

        generated.Should().Contain("knowledge.knowledge-item.read",
            "the boundary branch must name the same permission the CRUD generator emits");
        generated.Should().NotContain("knowledge.knowledgeitem.read");
    }
}
