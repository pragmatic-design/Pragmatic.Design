using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Transforms;

/// <summary>Reads a <c>[LoggerMessage]</c> method into the model its body is written from.</summary>
/// <remarks>
///     <para>
///         The rules are Microsoft's, so a call site moves from its generator to this one unchanged: a
///         static method takes the logger as a parameter, an instance method finds it in a parameter, a
///         field, a property or a primary-constructor parameter; the level is the attribute's or a
///         <c>LogLevel</c> parameter's; the first <c>Exception</c> parameter is the entry's exception;
///         placeholders match parameters by name, ignoring case and a leading <c>@</c>.
///     </para>
///     <para>
///         An invalid method is still returned, marked so: the template emits nothing for it, and the
///         analyzer, which has the locations, says why.
///     </para>
/// </remarks>
internal static class LogCallSiteTransform
{
    private const string LoggerFqn = "Microsoft.Extensions.Logging.ILogger";
    private const string LogLevelFqn = "Microsoft.Extensions.Logging.LogLevel";
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
        var loggerType = compilation.GetTypeByMetadataName(LoggerFqn);
        var levelType = compilation.GetTypeByMetadataName(LogLevelFqn);
        var exceptionType = compilation.GetTypeByMetadataName("System.Exception");
        if (loggerType is null || levelType is null || exceptionType is null)
            return null;

        var attribute = context.Attributes[0];
        var settings = ReadAttribute(attribute);

        var valid = method.IsPartialDefinition
                    && method.ReturnsVoid
                    && !method.IsGenericMethod
                    && method.Parameters.All(p => p.RefKind == RefKind.None);

        var segments = LogTemplateParser.Parse(settings.Message, out _);
        if (segments is null || segments.Any(s => s.IsPlaceholder && s.Alignment is not null))
            valid = false;

        // Roles first: a logger, a level and an exception are not properties by default.
        var loggerParameter = method.Parameters.FirstOrDefault(p => IsLogger(p.Type, loggerType));
        var levelParameter = settings.Level is null
            ? method.Parameters.FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p.Type, levelType))
            : null;
        var exceptionParameter = method.Parameters.FirstOrDefault(p => DerivesFrom(p.Type, exceptionType));

        var loggerExpression = loggerParameter?.Name ?? (method.IsStatic ? null : FindLogger(method.ContainingType, loggerType));
        if (loggerExpression is null)
            valid = false;

        var levelExpression = settings.Level is { } level
            ? LevelMemberPrefix + LevelName(level)
            : levelParameter?.Name;
        if (levelExpression is null)
            valid = false;

        var placeholders = segments?.Where(s => s.IsPlaceholder).ToList() ?? [];

        var parameters = ImmutableArray.CreateBuilder<LogParameterModel>();
        foreach (var parameter in method.Parameters)
        {
            ct.ThrowIfCancellationRequested();

            var role = SymbolEqualityComparer.Default.Equals(parameter, loggerParameter) ? LogParameterRole.Logger
                : SymbolEqualityComparer.Default.Equals(parameter, levelParameter) ? LogParameterRole.Level
                : SymbolEqualityComparer.Default.Equals(parameter, exceptionParameter) ? LogParameterRole.Exception
                : LogParameterRole.Property;

            var placeholder = placeholders.FirstOrDefault(s =>
                string.Equals(s.MatchName, parameter.Name, System.StringComparison.OrdinalIgnoreCase));

            var isProperty = role == LogParameterRole.Property
                             || (role == LogParameterRole.Exception && placeholder is not null);

            parameters.Add(Parameter(parameter, role, isProperty, placeholder?.Text ?? parameter.Name, compilation));
        }

        // Every placeholder names a property parameter.
        var parts = ImmutableArray.CreateBuilder<LogMessagePart>();
        foreach (var segment in segments ?? [])
        {
            if (!segment.IsPlaceholder)
            {
                parts.Add(new LogMessagePart(segment.Text, -1, ""));
                continue;
            }

            var index = IndexOfProperty(parameters, segment.MatchName);
            if (index < 0)
            {
                valid = false;
                continue;
            }

            parts.Add(new LogMessagePart(null, index, segment.Format ?? ""));
        }

        var eventName = settings.EventName ?? method.Name;

        return new LogCallSiteModel
        {
            Namespace = method.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            Containers = Containers(method.ContainingType, ct),
            MethodName = method.Name,
            Modifiers = Modifiers(syntax),
            Parameters = parameters.ToImmutable(),
            Template = settings.Message,
            Parts = parts.ToImmutable(),
            LoggerExpression = loggerExpression ?? "",
            LevelExpression = levelExpression ?? "",
            EventId = settings.EventId >= 0 ? settings.EventId : LogEventIds.Derive(eventName),
            EventName = eventName,
            SkipEnabledCheck = settings.SkipEnabledCheck,
            IsValid = valid && Containers(method.ContainingType, ct).All(c => c.IsPartial),
        };
    }

    private static int IndexOfProperty(ImmutableArray<LogParameterModel>.Builder parameters, string name)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Role is LogParameterRole.Property or LogParameterRole.Exception
                && string.Equals(parameters[i].Name, name, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
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

    private static bool IsLogger(ITypeSymbol type, INamedTypeSymbol logger)
        => SymbolEqualityComparer.Default.Equals(type, logger)
           || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, logger));

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;

        return false;
    }

    /// <summary>
    ///     The logger an instance method writes to: a field or a property of the type or a base, then a
    ///     primary-constructor parameter. One of each kind, or none: two loggers is a choice the
    ///     generator does not make.
    /// </summary>
    private static string? FindLogger(INamedTypeSymbol type, INamedTypeSymbol logger)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var members = current.GetMembers()
                .Where(m => !m.IsStatic && (SymbolEqualityComparer.Default.Equals(current, type) || m.DeclaredAccessibility != Accessibility.Private))
                .Where(m => m switch
                {
                    IFieldSymbol f => !f.IsImplicitlyDeclared && IsLogger(f.Type, logger),
                    IPropertySymbol p => IsLogger(p.Type, logger),
                    _ => false,
                })
                .ToList();

            if (members.Count == 1)
                return "this." + members[0].Name;
            if (members.Count > 1)
                return null;
        }

        var primary = type.InstanceConstructors
            .SelectMany(c => c.Parameters)
            .Where(p => IsLogger(p.Type, logger)
                        && p.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is ParameterSyntax { Parent.Parent: TypeDeclarationSyntax }))
            .ToList();

        return primary.Count == 1 ? primary[0].Name : null;
    }

    private static string Modifiers(MethodDeclarationSyntax syntax)
        => string.Join(" ", syntax.Modifiers
            .Select(m => m.Text)
            .Where(m => m != "partial"));

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

    private static Settings ReadAttribute(AttributeData attribute)
    {
        var settings = new Settings();
        var arguments = attribute.ConstructorArguments;

        // The five constructors: (), (int, LogLevel, string), (LogLevel, string), (LogLevel), (string).
        foreach (var argument in arguments)
        {
            switch (argument.Value)
            {
                case int id when argument.Type?.SpecialType == SpecialType.System_Int32:
                    settings.EventId = id;
                    break;
                case int level:
                    settings.Level = level;
                    break;
                case string message:
                    settings.Message = message;
                    break;
            }
        }

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "EventId" when named.Value.Value is int id:
                    settings.EventId = id;
                    break;
                case "EventName" when named.Value.Value is string name:
                    settings.EventName = name;
                    break;
                case "Level" when named.Value.Value is int level:
                    settings.Level = level;
                    break;
                case "Message" when named.Value.Value is string message:
                    settings.Message = message;
                    break;
                case "SkipEnabledCheck" when named.Value.Value is bool skip:
                    settings.SkipEnabledCheck = skip;
                    break;
            }
        }

        // LogLevel.None is the attribute's "not set": the level is a parameter then.
        if (settings.Level == 6)
            settings.Level = null;

        return settings;
    }

    private sealed class Settings
    {
        public int EventId { get; set; } = -1;
        public string? EventName { get; set; }
        public int? Level { get; set; }
        public string Message { get; set; } = "";
        public bool SkipEnabledCheck { get; set; }
    }
}
