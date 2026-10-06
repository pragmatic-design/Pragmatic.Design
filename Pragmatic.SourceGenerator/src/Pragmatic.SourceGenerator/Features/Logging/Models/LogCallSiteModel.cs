using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Logging.Models;

/// <summary>One <c>[LoggerMessage]</c> method, as the generator writes its body and its state.</summary>
internal sealed record LogCallSiteModel
{
    /// <summary>The namespace of the outermost container; empty for the global namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>The types the method is declared in, outermost first; the last one declares the method.</summary>
    public required EquatableArray<LogContainerModel> Containers { get; init; }

    /// <summary>The method's name.</summary>
    public required string MethodName { get; init; }

    /// <summary>Its modifiers before <c>partial</c>, as the implementation must repeat them: <c>public static</c>.</summary>
    public required string Modifiers { get; init; }

    /// <summary>Its parameters, in declaration order.</summary>
    public required EquatableArray<LogParameterModel> Parameters { get; init; }

    /// <summary>The message template as written.</summary>
    public required string Template { get; init; }

    /// <summary>The message, part by part.</summary>
    public required EquatableArray<LogMessagePart> Parts { get; init; }

    /// <summary>The expression that reaches the logger: a parameter, a field, a property.</summary>
    public required string LoggerExpression { get; init; }

    /// <summary>The level: a <c>LogLevel</c> member access, or the name of the level parameter.</summary>
    public required string LevelExpression { get; init; }

    /// <summary>The event id: the attribute's, or one derived from the event name.</summary>
    public required int EventId { get; init; }

    /// <summary>The event name: the attribute's, or the method's name.</summary>
    public required string EventName { get; init; }

    /// <summary>Whether the body skips the <c>IsEnabled</c> check.</summary>
    public required bool SkipEnabledCheck { get; init; }

    /// <summary>
    ///     Whether the method is the implementation part of a <c>partial</c> declaration the user wrote;
    ///     false for a call site generated whole, inside a type another template writes.
    /// </summary>
    public bool IsPartialImplementation { get; init; } = true;

    /// <summary>
    ///     Whether a body can be generated. When not, nothing is emitted and the analyzer reports why,
    ///     where the method is written.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>Whether every property is written by the state itself; see <c>IUtf8LogState.IsSelfContained</c>.</summary>
    public bool IsSelfContained
    {
        get
        {
            foreach (var parameter in Parameters)
                if (parameter.IsProperty && parameter.Kind == LogValueKind.Object && !parameter.IsMasked)
                    return false;

            return true;
        }
    }

    /// <summary>What the call sites of one type share: the file they are written to.</summary>
    public string TypeKey
    {
        get
        {
            var key = Namespace;
            foreach (var container in Containers)
                key += "/" + container.Name;
            return key;
        }
    }
}
