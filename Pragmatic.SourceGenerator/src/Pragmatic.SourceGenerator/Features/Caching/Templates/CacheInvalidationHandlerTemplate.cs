using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Caching.Models;

namespace Pragmatic.SourceGenerator.Features.Caching.Templates;

/// <summary>
///     The <c>IDomainEventHandler&lt;TEvent&gt;</c> that <c>[InvalidatesCache]</c> on a domain event
///     has always promised: the dispatcher invokes it, it invalidates the declared tags and keys.
/// </summary>
/// <remarks>
///     <para>
///         The mutation path is different and stays as it is: there the attribute makes the mutation
///         itself an <c>ICacheInvalidator</c>, and the mutation invoker calls it after the mutation
///         succeeds. An event has no invoker — only a dispatcher, which knows about handlers — so the
///         bridge has to be a handler type of its own.
///     </para>
///     <para>
///         The handler holds an <see cref="System.IServiceProvider"/> rather than an
///         <c>ICacheStack</c>: caching may be referenced but never registered, and resolving lazily
///         is what keeps an application without <c>AddPragmaticCaching()</c> starting. All of that
///         lives in <c>CacheInvalidationRunner</c>; what is generated here is the declared data.
///     </para>
/// </remarks>
internal sealed class CacheInvalidationHandlerTemplate : CSharpTemplate
{
    private const string HandlerSuffix = "CacheInvalidationHandler";

    private readonly InvalidatesModel _model;

    public CacheInvalidationHandlerTemplate(InvalidatesModel model) => _model = model;

    /// <summary>
    ///     The generated handler's simple name. Single source of truth: the Composition feature
    ///     registers this type by name before it exists, because a generator cannot resolve a symbol
    ///     it is itself creating.
    /// </summary>
    public static string HandlerTypeName(InvalidatesModel model)
        => NamingHelper.AppendSuffix(model.TypeName, HandlerSuffix);

    /// <summary>The generated handler's fully qualified name.</summary>
    public static string HandlerFullTypeName(InvalidatesModel model)
        => string.IsNullOrEmpty(model.Namespace)
            ? $"global::{HandlerTypeName(model)}"
            : $"global::{model.Namespace}.{HandlerTypeName(model)}";

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Caching";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[InvalidatesCache] on domain event {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, HandlerSuffix, _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsDomainEvent;

    public override void RenderFile()
    {
        AddUsings("System", "System.Threading", "System.Threading.Tasks");

        AppendNamespace(_model.Namespace);
        AppendLine();

        RenderHandlerClass();
    }

    private void RenderHandlerClass()
    {
        XmlSummary(
            $"Invalidates the cache entries declared by [InvalidatesCache] on {_model.TypeName} when it is dispatched.");

        var handlerName = HandlerTypeName(_model);

        Class(handlerName, () => RenderBody(handlerName),
            null,
            [$"global::Pragmatic.Events.IDomainEventHandler<{_model.TypeFqn}>"],
            AccessModifier.Internal,
            new ClassModifiers { Sealed = true });
    }

    private void RenderBody(string handlerName)
    {
        Field("_services", "global::System.IServiceProvider", isReadOnly: true);
        AppendLine();

        Constructor(handlerName,
            () => AppendLine("_services = services;"),
            [new MethodParameter("global::System.IServiceProvider", "services")]);

        RenderHandleAsync();
    }

    private void RenderHandleAsync()
    {
        XmlSummary("Invalidates the declared tags and keys against every cache stack they can be in.");
        XmlParam("event", "The dispatched event; placeholders in tags and keys read from it.");
        XmlParam("ct", "Cancellation token.");

        var parameters = new List<MethodParameter>
        {
            new(_model.TypeFqn, "event"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("HandleAsync", RenderHandleAsyncBody,
            "global::System.Threading.Tasks.Task",
            parameters);
    }

    private void RenderHandleAsyncBody()
    {
        var category = _model.CategoryTypeFqn is not null ? $"typeof({_model.CategoryTypeFqn})" : "null";

        AppendLine("return global::Pragmatic.Caching.CacheInvalidationRunner.InvalidateAsync(");
        IncreaseIndent();
        AppendLine("_services,");
        AppendLine($"{category},");
        AppendLine($"{RenderLiteralList(_model.Tags)},");
        AppendLine($"{RenderLiteralList(_model.Keys)},");
        AppendLine("ct);");
        DecreaseIndent();
    }

    private string RenderLiteralList(EquatableArray<string> templates)
    {
        var literals = templates
            .Select(t => CachePlaceholderExpander.Expand(t, _model.Properties, "@event"))
            .ToList();

        // Collection expression: the target type is IReadOnlyList<string> on the runner, and the
        // generated code runs on net10.0 where [] and [a, b] are the house style.
        return literals.Count == 0 ? "[]" : $"[{string.Join(", ", literals)}]";
    }
}
