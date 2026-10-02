using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the cascade handler + its DI registration. An unregistered handler never runs, and a
///     non-keyed DbContext is unresolvable in host mode.
/// </summary>
public class CascadeHandlerTemplateTests
{
    private static CascadeModel Model(string? boundary = "global::MyApp.Billing.BillingBoundary", string? condition = null) => new()
    {
        Namespace = "MyApp.Billing.Entities",
        SourceTypeName = "RoomType",
        SourceFullTypeName = "MyApp.Catalog.Entities.RoomType",
        SourceQualifiedTypeName = "global::MyApp.Catalog.Entities.RoomType",
        SourceProperty = "BaseRate",
        TargetTypeName = "LineItem",
        TargetFullTypeName = "global::MyApp.Billing.Entities.LineItem",
        TargetProperty = "UnitPrice",
        TargetPropertyTypeName = "decimal",
        ForeignKeyProperty = "RoomTypeId",
        ForeignKeyTypeName = "global::System.Guid?",
        TargetBoundaryTypeFullName = boundary,
        Condition = condition
    };

    [Fact]
    public void Handler_WithBoundary_InjectsKeyedDbContext()
    {
        // Host mode registers the DbContext only as AddKeyedScoped by boundary; a non-keyed
        // ctor param would throw at resolution. The handler must request the boundary-keyed DbContext.
        var source = new CascadeHandlerTemplate(Model()).RenderOutput().Text;

        source.Should().Contain("[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::MyApp.Billing.BillingBoundary))]");
        source.Should().Contain("global::Microsoft.EntityFrameworkCore.DbContext dbContext");
    }

    [Fact]
    public void Handler_WithoutBoundary_UsesPlainDbContext()
    {
        // Console mode registers an unkeyed DbContext; when no boundary is resolvable, keep the plain param.
        var source = new CascadeHandlerTemplate(Model(boundary: null)).RenderOutput().Text;

        source.Should().NotContain("FromKeyedServices");
        source.Should().Contain("public LineItemUnitPriceCascadeHandler(global::Microsoft.EntityFrameworkCore.DbContext dbContext)");
    }

    [Fact]
    public void Handler_WithCondition_ParenthesisesTheBooleanMember()
    {
        // The condition composes with the FK predicate and must be parenthesised.
        var source = new CascadeHandlerTemplate(Model(condition: "IsPending")).RenderOutput().Text;

        source.Should().Contain("e.RoomTypeId == entityId && (e.IsPending)");
    }

    [Fact]
    public void Registration_RegistersHandlerAsDomainEventHandler()
    {
        // Without a registration the domain-event dispatcher never resolves the handler and the
        // cascade silently does nothing.
        var source = new CascadeHandlerRegistrationTemplate(ImmutableArray.Create(Model()))
            .RenderOutput().Text;

        source.Should().Contain("CascadeHandlers(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.Should().Contain(
            "services.AddScoped<global::Pragmatic.Events.IDomainEventHandler<global::Pragmatic.Events.EntityPropertyChanged<global::MyApp.Catalog.Entities.RoomType>>, global::MyApp.Billing.Entities.LineItemUnitPriceCascadeHandler>();");
    }
}
