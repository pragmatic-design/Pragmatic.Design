using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Temporal.AspNetCore.Tests;

/// <summary>Minimal provider context for testing IModelBinderProvider implementations.</summary>
internal sealed class TestModelBinderProviderContext(ModelMetadata metadata) : ModelBinderProviderContext
{
    public override BindingInfo BindingInfo { get; } = new();

    public override ModelMetadata Metadata { get; } = metadata;

    public override IModelMetadataProvider MetadataProvider { get; } = new EmptyModelMetadataProvider();

    public override IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

    public override IModelBinder CreateBinder(ModelMetadata metadata)
    {
        throw new NotSupportedException("Nested binders are not exercised by these tests.");
    }
}
