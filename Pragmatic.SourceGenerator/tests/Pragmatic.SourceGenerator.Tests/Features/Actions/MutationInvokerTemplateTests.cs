using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Compositions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

public class MutationInvokerTemplateTests
{
                    [Fact]
    public void RenderOutput_ContainsAllStandardMethods()
    {
        var model = BuildModel("Sales", "CreateOrder");
        var source = Render(model);

        source.Should().Contain("LoadEntityAsync");
        source.Should().Contain("CreateEntity");
        source.Should().Contain("GetMode");
        source.Should().Contain("SaveChangesAsync");
    }

    [Fact]
    public void ComputedDefaults_GeneratesApplyComputedDefaultsAsync()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            ComputedDefaults = new ComputedDefaultContribution
            {
                Properties = ImmutableArray.Create(new ComputedDefaultPropertyModel
                {
                    PropertyName = "OrderNumber",
                    SetterName = "SetOrderNumber",
                    ValueTypeFqn = "string",
                    GeneratorTypeFqn = "global::Sales.OrderNumberGenerator",
                    EntityTypeFqn = "global::Sales.Order"
                })
            }
        };

        var source = Render(model);

        source.Should().Contain("ApplyComputedDefaultsAsync");
        source.Should().Contain("IDefaultValueGenerator<global::Sales.Order, string>");
        source.Should().Contain("GenerateAsync(entity, context, ct)");
        source.Should().Contain("entity.SetOrderNumber(orderNumberValue)");
    }

    [Fact]
    public void NoComputedDefaults_DoesNotGenerateApplyComputedDefaultsAsync()
    {
        var model = BuildModel("Sales", "CreateOrder");
        var source = Render(model);

        source.Should().NotContain("ApplyComputedDefaultsAsync");
    }

    [Fact]
    public void RaisedEvents_GeneratesCollectRaisedEventsOverride()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            RaisedEvents =
            [
                new RaisedEventModel
                {
                    EventFullName = "global::Sales.OrderPlaced",
                    ConstructorArguments = ["entity.Id", "mutation.Amount"]
                }
            ]
        };

        var source = Render(model);

        source.Should().Contain("CollectRaisedEvents(global::Sales.CreateOrder mutation, global::Sales.Order entity)");
        source.Should().Contain("=> [new global::Sales.OrderPlaced(entity.Id, mutation.Amount)];");
    }

    [Fact]
    public void NoRaisedEvents_DoesNotGenerateCollectRaisedEvents()
    {
        Render(BuildModel("Sales", "CreateOrder")).Should().NotContain("CollectRaisedEvents");
    }

    [Fact]
    public void ComputedDefaults_PublicSetter_UsesDirectAssignment()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            ComputedDefaults = new ComputedDefaultContribution
            {
                Properties = ImmutableArray.Create(new ComputedDefaultPropertyModel
                {
                    PropertyName = "Status",
                    SetterName = "Status", // Same as property = public setter
                    ValueTypeFqn = "int",
                    GeneratorTypeFqn = "global::Sales.StatusGenerator",
                    EntityTypeFqn = "global::Sales.Order"
                })
            }
        };

        var source = Render(model);

        source.Should().Contain("entity.Status = statusValue");
    }

    [Fact]
    public void ComputedDefaults_MultipleProperties_GeneratesAll()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            ComputedDefaults = new ComputedDefaultContribution
            {
                Properties = ImmutableArray.Create(
                    new ComputedDefaultPropertyModel
                    {
                        PropertyName = "OrderNumber",
                        SetterName = "SetOrderNumber",
                        ValueTypeFqn = "string",
                        GeneratorTypeFqn = "global::Sales.OrderNumberGenerator",
                        EntityTypeFqn = "global::Sales.Order"
                    },
                    new ComputedDefaultPropertyModel
                    {
                        PropertyName = "CreatedDate",
                        SetterName = "CreatedDate",
                        ValueTypeFqn = "global::System.DateTimeOffset",
                        GeneratorTypeFqn = "global::Sales.DateGenerator",
                        EntityTypeFqn = "global::Sales.Order"
                    })
            }
        };

        var source = Render(model);

        source.Should().Contain("orderNumberGenerator");
        source.Should().Contain("createdDateGenerator");
    }

    [Fact]
    public void Presets_GeneratesApplyPresetsAsync()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            Presets = new PresetContribution
            {
                Providers = ImmutableArray.Create(new PresetProviderModel
                {
                    ProviderTypeFqn = "global::Sales.OrderLinePresetProvider",
                    Order = 0
                })
            }
        };

        var source = Render(model);

        source.Should().Contain("ApplyPresetsAsync");
        source.Should().Contain("OrderLinePresetProvider");
        source.Should().Contain("CreatePresetsAsync(entity, context, ct)");
        source.Should().Contain("_unitOfWork.Add(preset)");
    }

    [Fact]
    public void NoPresets_DoesNotGenerateApplyPresetsAsync()
    {
        var model = BuildModel("Sales", "CreateOrder");
        var source = Render(model);

        source.Should().NotContain("ApplyPresetsAsync");
    }

    [Fact]
    public void Presets_MultipleProviders_GeneratesAll()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            Presets = new PresetContribution
            {
                Providers = ImmutableArray.Create(
                    new PresetProviderModel { ProviderTypeFqn = "global::Sales.OrderLinePresetProvider", Order = 0 },
                    new PresetProviderModel { ProviderTypeFqn = "global::Sales.ShippingPresetProvider", Order = 1 })
            }
        };

        var source = Render(model);

        source.Should().Contain("OrderLinePresetProvider");
        source.Should().Contain("ShippingPresetProvider");
    }

    [Fact]
    public void Render_WithInvariants_EmitsCheckInvariantsOverride()
    {
        var model = BuildModel("Sales", "CreateOrder") with
        {
            Invariants = ImmutableArray.Create(
                new InvariantModel { MethodName = "HasPositiveTotal", Message = "Total must be positive" },
                new InvariantModel { MethodName = "HasLines", Message = null })
        };

        var source = Render(model);

        source.Should().Contain("protected override global::Pragmatic.Result.IError? CheckInvariants(");
        source.Should().Contain("if (!entity.HasPositiveTotal())");
        source.Should().Contain(
            "new global::Pragmatic.Actions.Mutation.InvariantViolationError(\"HasPositiveTotal\", \"Total must be positive\")");
        source.Should().Contain("if (!entity.HasLines())");
        source.Should().Contain("return null;");
    }

    [Fact]
    public void Render_WithoutInvariants_DoesNotEmitCheckInvariants()
    {
        var source = Render(BuildModel("Sales", "CreateOrder"));

        source.Should().NotContain("CheckInvariants");
    }

    private static string Render(MutationModel model)
    {
        var template = new MutationInvokerTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static MutationModel BuildModel(string ns, string name,
        MutationModeValue mode = MutationModeValue.Create)
    {
        return new MutationModel
        {
            Namespace = ns,
            TypeName = name,
            FullTypeName = $"global::{ns}.{name}",
            Accessibility = "public",
            EntityTypeName = "Order",
            EntityFullTypeName = $"global::{ns}.Order",
            EntityIdTypeName = "global::System.Guid",
            Mode = mode,
            IdPropertyName = "Id"
        };
    }
}
