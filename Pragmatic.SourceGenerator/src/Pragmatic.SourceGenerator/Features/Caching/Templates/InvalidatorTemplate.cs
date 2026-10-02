using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Caching.Models;

namespace Pragmatic.SourceGenerator.Features.Caching.Templates;

internal sealed class InvalidatorTemplate : CSharpTemplate
{
    private readonly InvalidatesModel _model;

    public InvalidatorTemplate(InvalidatesModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Caching";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[InvalidatesCache] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "CacheInvalidator", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsings("System.Threading", "System.Threading.Tasks");

        AppendNamespace(_model.Namespace);
        AppendLine();

        RenderInvalidatorClass();
    }

    private void RenderInvalidatorClass()
    {
        XmlSummary($"Cache invalidation implementation for {_model.TypeName}.");

        var interfaces = new List<string> { "global::Pragmatic.Caching.ICacheInvalidator" };
        var access = TemplateHelpers.ParseAccessibility(_model.Accessibility);
        var mods = new ClassModifiers { Partial = true };

        void body()
        {
            RenderCategoryProperty();
            RenderInvalidateAsyncMethod();
        }

        if (_model.TypeKind == "record")
            Record(_model.TypeName, body, null, interfaces, access, mods);
        else
            Class(_model.TypeName, body, null, interfaces, access, mods);
    }

    private void RenderCategoryProperty()
    {
        if (_model.CategoryTypeFqn is not null)
        {
            ExpressionProperty("InvalidationCategory", "global::System.Type?",
                $"typeof({_model.CategoryTypeFqn})");
            AppendLine();
        }
    }

    private void RenderInvalidateAsyncMethod()
    {
        XmlSummary("Invalidates cache entries based on the mutation data.");
        XmlParam("cache", "The cache stack to invalidate.");
        XmlParam("ct", "Cancellation token.");

        var parameters = new List<MethodParameter>
        {
            new("global::Pragmatic.Caching.ICacheStack", "cache"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("InvalidateAsync", RenderInvalidateAsyncBody,
            "global::System.Threading.Tasks.ValueTask",
            parameters,
            modifiers: new MethodModifiers { IsAsync = true });
    }

    private void RenderInvalidateAsyncBody()
    {
        foreach (var tag in _model.Tags)
            AppendLine($"await cache.InvalidateByTagAsync({ExpandPlaceholder(tag)}, ct).ConfigureAwait(false);");

        foreach (var key in _model.Keys)
            AppendLine($"await cache.RemoveAsync({ExpandPlaceholder(key)}, ct).ConfigureAwait(false);");
    }

    private string ExpandPlaceholder(string template)
        => CachePlaceholderExpander.Expand(template, _model.Properties, "this");
}
