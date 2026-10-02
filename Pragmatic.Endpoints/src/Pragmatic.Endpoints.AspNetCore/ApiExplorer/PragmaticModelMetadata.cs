using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     The model metadata of a parameter the API explorer did not bind itself: the type, and defaults for
///     everything else.
/// </summary>
/// <remarks>
///     ⚠️ Not optional, although nothing here says anything. The OpenAPI document reads
///     <c>ModelMetadata.ModelType</c> for every <c>string</c> parameter and <c>ContainerType</c> for every
///     form field, without a null check — a description without metadata throws while the document is
///     built. ASP.NET's minimal-API provider fills the same gap with an internal type of the same shape,
///     which is what this mirrors.
/// </remarks>
internal sealed class PragmaticModelMetadata(ModelMetadataIdentity identity) : ModelMetadata(identity)
{
    public override IReadOnlyDictionary<object, object> AdditionalValues { get; } = new Dictionary<object, object>();

    public override string? BinderModelName => null;

    public override Type? BinderType => null;

    public override BindingSource? BindingSource => null;

    public override bool ConvertEmptyStringToNull => false;

    public override string? DataTypeName => null;

    public override string? Description => null;

    public override string? DisplayFormatString => null;

    public override string? DisplayName => null;

    public override string? EditFormatString => null;

    public override ModelMetadata? ElementMetadata => null;

    public override IEnumerable<KeyValuePair<EnumGroupAndName, string>>? EnumGroupedDisplayNamesAndValues => null;

    public override IReadOnlyDictionary<string, string>? EnumNamesAndValues => null;

    public override bool HasNonDefaultEditFormat => false;

    public override bool HideSurroundingHtml => false;

    public override bool HtmlEncode => false;

    public override bool IsBindingAllowed => true;

    public override bool IsBindingRequired => false;

    public override bool IsEnum => false;

    public override bool IsFlagsEnum => false;

    public override bool IsReadOnly => false;

    public override bool IsRequired => false;

    public override ModelBindingMessageProvider ModelBindingMessageProvider { get; } = new DefaultModelBindingMessageProvider();

    public override string? NullDisplayText => null;

    public override int Order => 0;

    public override string? Placeholder => null;

    public override ModelPropertyCollection Properties { get; } = new([]);

    public override IPropertyFilterProvider? PropertyFilterProvider => null;

    public override Func<object, object>? PropertyGetter => null;

    public override Action<object, object?>? PropertySetter => null;

    public override bool ShowForDisplay => false;

    public override bool ShowForEdit => false;

    public override string? SimpleDisplayProperty => null;

    public override string? TemplateHint => null;

    public override bool ValidateChildren => false;

    public override IReadOnlyList<object> ValidatorMetadata { get; } = [];
}
