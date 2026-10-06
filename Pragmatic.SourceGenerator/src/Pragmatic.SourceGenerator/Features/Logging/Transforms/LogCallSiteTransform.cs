using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Transforms;

/// <summary>Turns a <c>[LoggerMessage]</c> method into the model its body is written from.</summary>
/// <remarks>
///     The rules — where the logger is, which parameter is the level, what a placeholder names — are
///     <see cref="LogCallSiteReader" />'s, shared with the analyzer. A method the reader finds a blocking
///     problem with is still returned, marked invalid: nothing is emitted for it, and the analyzer says
///     why, where it is written.
/// </remarks>
internal static class LogCallSiteTransform
{
    private const string LevelMemberPrefix = "global::Microsoft.Extensions.Logging.LogLevel.";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly string[] LevelNames =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    public static LogCallSiteModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not IMethodSymbol method || context.TargetNode is not MethodDeclarationSyntax syntax)
            return null;

        var compilation = context.SemanticModel.Compilation;
        if (LogCallSiteReader.Read(method, context.Attributes[0], compilation) is not { } shape)
            return null;

        var placeholders = shape.Segments?.Where(s => s.IsPlaceholder).ToList() ?? [];

        var parameters = ImmutableArray.CreateBuilder<LogParameterModel>();
        foreach (var parameter in method.Parameters)
        {
            ct.ThrowIfCancellationRequested();

            var role = LogCallSiteReader.RoleOf(parameter, shape) switch
            {
                "logger" => LogParameterRole.Logger,
                "level" => LogParameterRole.Level,
                "exception" => LogParameterRole.Exception,
                _ => LogParameterRole.Property,
            };

            var placeholder = placeholders.FirstOrDefault(s =>
                string.Equals(s.MatchName, parameter.Name, System.StringComparison.OrdinalIgnoreCase));

            var isProperty = role == LogParameterRole.Property
                             || (role == LogParameterRole.Exception && placeholder is not null);

            parameters.Add(Parameter(parameter, role, isProperty, placeholder?.Text ?? parameter.Name, compilation));
        }

        var parts = ImmutableArray.CreateBuilder<LogMessagePart>();
        foreach (var segment in shape.Segments ?? [])
        {
            if (!segment.IsPlaceholder)
            {
                parts.Add(new LogMessagePart(segment.Text, -1, ""));
                continue;
            }

            var named = LogCallSiteReader.PropertyNamed(method, shape, segment.MatchName);
            var index = named is null ? -1 : method.Parameters.IndexOf(named);
            if (index >= 0)
                parts.Add(new LogMessagePart(null, index, segment.Format ?? ""));
        }

        var eventName = shape.EventName ?? method.Name;

        return new LogCallSiteModel
        {
            Namespace = method.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            Containers = Containers(method.ContainingType, ct),
            MethodName = method.Name,
            Modifiers = string.Join(" ", syntax.Modifiers.Select(m => m.Text).Where(m => m != "partial")),
            Parameters = parameters.ToImmutable(),
            Template = shape.Message,
            Parts = parts.ToImmutable(),
            LoggerExpression = shape.LoggerParameter?.Name ?? shape.LoggerMember ?? "",
            LevelExpression = shape.Level is { } level ? LevelMemberPrefix + LevelName(level) : shape.LevelParameter?.Name ?? "",
            EventId = shape.EventId >= 0 ? shape.EventId : LogEventIds.Derive(eventName),
            EventName = eventName,
            SkipEnabledCheck = shape.SkipEnabledCheck,
            IsValid = shape.IsGeneratable,
        };
    }

    private static LogParameterModel Parameter(
        IParameterSymbol parameter, LogParameterRole role, bool isProperty, string key, Compilation compilation)
    {
        var nullable = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapped
            ? wrapped.TypeArguments[0]
            : null;

        var (kind, jsonFormat, numberType) = LogValueKinds.Of(nullable ?? parameter.Type, compilation);

        return new LogParameterModel
        {
            Name = parameter.Name,
            Type = parameter.Type.ToDisplayString(TypeFormat),
            Role = role,
            IsProperty = isProperty,
            Key = key,
            Kind = kind,
            IsNullableValueType = nullable is not null,
            IsMasked = IsMasked(parameter),
            JsonFormat = jsonFormat,
            NumberType = numberType,
        };
    }

    /// <summary>Whether the parameter says its argument must not be logged.</summary>
    public static bool IsMasked(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            var ns = attribute.AttributeClass?.ContainingNamespace?.ToDisplayString();

            if ((name == "NotLoggedAttribute" && ns == "Pragmatic")
                || (name == "PersonalDataAttribute" && ns == "Pragmatic.Privacy"))
                return true;
        }

        return false;
    }

    private static EquatableArray<LogContainerModel> Containers(INamedTypeSymbol type, CancellationToken ct)
    {
        var chain = new List<LogContainerModel>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            ct.ThrowIfCancellationRequested();

            var isPartial = current.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(ct))
                .OfType<TypeDeclarationSyntax>()
                .Any(d => d.Modifiers.Any(m => m.Text == "partial"));

            var name = current.TypeParameters.Length == 0
                ? current.Name
                : current.Name + "<" + string.Join(", ", current.TypeParameters.Select(t => t.Name)) + ">";

            chain.Add(new LogContainerModel(Keyword(current), name, current.IsStatic, isPartial));
        }

        chain.Reverse();
        return chain.ToImmutableArray();
    }

    private static string Keyword(INamedTypeSymbol type)
        => type switch
        {
            { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            _ => "class",
        };

    private static string LevelName(int level)
        => level >= 0 && level < LevelNames.Length ? LevelNames[level] : "None";
}
