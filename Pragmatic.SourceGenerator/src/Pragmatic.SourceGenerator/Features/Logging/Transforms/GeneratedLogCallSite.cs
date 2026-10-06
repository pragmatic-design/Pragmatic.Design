using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Transforms;

/// <summary>
///     The model of a call site a template writes whole, inside a type it generates: the handler pipeline,
///     the saga orchestrator, the attachment purge job.
/// </summary>
/// <remarks>
///     There is no user declaration to read, so the model is built from what the template knows: the
///     method's name, its parameters by type name, its message. The method is written with its body and
///     no <c>partial</c>, since nothing declares it elsewhere.
/// </remarks>
internal static class GeneratedLogCallSite
{
    private const string LevelMemberPrefix = "global::Microsoft.Extensions.Logging.LogLevel.";

    /// <param name="methodName">The method the generated code calls.</param>
    /// <param name="modifiers">Its modifiers: <c>private</c>, <c>private static</c>.</param>
    /// <param name="loggerExpression">How the method reaches the logger: a field, or the name of a logger parameter.</param>
    /// <param name="level">A <c>LogLevel</c> member name.</param>
    /// <param name="message">The template; a placeholder names a parameter, ignoring case.</param>
    /// <param name="parameters">The method's parameters, the logger one included when the method takes it.</param>
    /// <param name="exception">The name of a trailing exception parameter, if the method takes one.</param>
    public static LogCallSiteModel Create(
        string methodName,
        string modifiers,
        string loggerExpression,
        string level,
        string message,
        (string Type, string Name)[] parameters,
        string? exception = null)
    {
        var segments = LogTemplateParser.Parse(message, out var error)
                       ?? throw new System.InvalidOperationException($"The template of {methodName} is malformed: {error}");

        var models = ImmutableArray.CreateBuilder<LogParameterModel>();
        foreach (var (type, name) in parameters)
        {
            var isLogger = name == loggerExpression;
            var placeholder = segments.FirstOrDefault(s => s.IsPlaceholder
                && string.Equals(s.MatchName, name, System.StringComparison.OrdinalIgnoreCase));
            var (kind, jsonFormat, numberType) = LogValueKinds.OfTypeName(type);

            models.Add(new LogParameterModel
            {
                Name = name,
                Type = type,
                Role = isLogger ? LogParameterRole.Logger : LogParameterRole.Property,
                IsProperty = !isLogger,
                Key = placeholder?.Text ?? name,
                Kind = kind,
                IsNullableValueType = false,
                IsMasked = false,
                JsonFormat = jsonFormat,
                NumberType = numberType,
            });
        }

        if (exception is not null)
        {
            models.Add(new LogParameterModel
            {
                Name = exception,
                Type = "global::System.Exception",
                Role = LogParameterRole.Exception,
                IsProperty = false,
                Key = exception,
                Kind = LogValueKind.Object,
                IsNullableValueType = false,
                IsMasked = false,
                JsonFormat = "",
                NumberType = "",
            });
        }

        var parts = ImmutableArray.CreateBuilder<LogMessagePart>();
        foreach (var segment in segments)
        {
            if (!segment.IsPlaceholder)
            {
                parts.Add(new LogMessagePart(segment.Text, -1, ""));
                continue;
            }

            var index = -1;
            for (var i = 0; i < models.Count; i++)
            {
                if (models[i].IsProperty && string.Equals(models[i].Name, segment.MatchName, System.StringComparison.OrdinalIgnoreCase))
                    index = i;
            }

            if (index < 0)
                throw new System.InvalidOperationException($"The template of {methodName} names {{{segment.Text}}}, which is no parameter.");

            parts.Add(new LogMessagePart(null, index, segment.Format ?? ""));
        }

        return new LogCallSiteModel
        {
            Namespace = "",
            Containers = EquatableArray<LogContainerModel>.Empty,
            MethodName = methodName,
            Modifiers = modifiers,
            Parameters = models.ToImmutable(),
            Template = message,
            Parts = parts.ToImmutable(),
            LoggerExpression = loggerExpression,
            LevelExpression = LevelMemberPrefix + level,
            EventId = LogEventIds.Derive(methodName),
            EventName = methodName,
            SkipEnabledCheck = false,
            IsValid = true,
            IsPartialImplementation = false,
        };
    }
}
