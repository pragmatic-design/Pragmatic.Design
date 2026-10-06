using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Reads a <c>[LoggerMessage]</c> method the way Microsoft's generator reads one, and lists what is
///     wrong with it.
/// </summary>
/// <remarks>
///     <para>
///         The rules, so a call site moves from Microsoft's generator to Pragmatic's unchanged: a static
///         method takes the logger as a parameter; an instance method finds it in a parameter, then in one
///         field or property of the type or a base, then in one primary-constructor parameter; the level
///         is the attribute's, or a <c>LogLevel</c> parameter's; the first <c>Exception</c> parameter is
///         the entry's exception; a placeholder matches a parameter by name, ignoring case and a leading
///         <c>@</c>.
///     </para>
///     <para>
///         One reader for the generator and the analyzer: two would be two answers to whether a call site
///         is valid, and the analyzer would report errors on methods the generator wrote a body for.
///     </para>
/// </remarks>
internal static class LogCallSiteReader
{
    private const int LogLevelNone = 6;

    public static LogCallSiteShape? Read(IMethodSymbol method, AttributeData attribute, Compilation compilation)
    {
        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        var logLevel = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LogLevel");
        var exception = compilation.GetTypeByMetadataName("System.Exception");
        if (logger is null || logLevel is null || exception is null)
            return null;

        var shape = new LogCallSiteShape();
        ReadAttribute(attribute, shape);

        if (!method.IsPartialDefinition || !method.ReturnsVoid || method.IsGenericMethod
            || method.Parameters.Any(p => p.RefKind != RefKind.None))
            shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.WrongShape, method.Name));

        for (var container = method.ContainingType; container is not null; container = container.ContainingType)
        {
            var isPartial = container.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(d => d.Modifiers.Any(m => m.Text == "partial"));
            if (!isPartial)
                shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.ContainerNotPartial, container.Name));
        }

        shape.LoggerParameter = method.Parameters.FirstOrDefault(p => IsLogger(p.Type, logger));
        if (shape.LoggerParameter is null && !method.IsStatic)
            shape.LoggerMember = FindLogger(method.ContainingType, logger);
        if (shape.LoggerParameter is null && shape.LoggerMember is null)
            shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.NoLogger, method.Name));

        if (shape.Level is null)
        {
            shape.LevelParameter = method.Parameters.FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p.Type, logLevel));
            if (shape.LevelParameter is null)
                shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.NoLevel, method.Name));
        }

        shape.ExceptionParameter = method.Parameters.FirstOrDefault(p => DerivesFrom(p.Type, exception));

        shape.Segments = LogTemplateParser.Parse(shape.Message, out var error);
        if (shape.Segments is null)
        {
            shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.MalformedTemplate, error ?? ""));
            return shape;
        }

        foreach (var segment in shape.Segments.Where(s => s.IsPlaceholder))
        {
            if (segment.Alignment is not null)
                shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.MalformedTemplate,
                    $"'{{{segment.Text},{segment.Alignment}}}' sets an alignment, which a structured property has no use for"));

            if (PropertyNamed(method, shape, segment.MatchName) is null)
                shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.PlaceholderWithoutParameter, segment.Text));
        }

        foreach (var parameter in method.Parameters)
        {
            if (RoleOf(parameter, shape) is not null)
                continue;

            var named = shape.Segments.Any(s => s.IsPlaceholder
                && string.Equals(s.MatchName, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (!named)
                shape.Problems.Add(new LogCallSiteProblem(LogCallSiteProblemKind.ParameterNotInTemplate, parameter.Name));
        }

        return shape;
    }

    /// <summary>The role a parameter has besides being a property: "logger", "level", "exception", or null.</summary>
    public static string? RoleOf(IParameterSymbol parameter, LogCallSiteShape shape)
        => SymbolEqualityComparer.Default.Equals(parameter, shape.LoggerParameter) ? "logger"
            : SymbolEqualityComparer.Default.Equals(parameter, shape.LevelParameter) ? "level"
            : SymbolEqualityComparer.Default.Equals(parameter, shape.ExceptionParameter) ? "exception"
            : null;

    /// <summary>The parameter a placeholder names, among those that can be properties.</summary>
    public static IParameterSymbol? PropertyNamed(IMethodSymbol method, LogCallSiteShape shape, string name)
        => method.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            && RoleOf(p, shape) is null or "exception");

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

    /// <summary>One field or property of the type or a base, else one primary-constructor parameter.</summary>
    private static string? FindLogger(INamedTypeSymbol type, INamedTypeSymbol logger)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var members = current.GetMembers()
                .Where(m => !m.IsStatic
                            && (SymbolEqualityComparer.Default.Equals(current, type) || m.DeclaredAccessibility != Accessibility.Private))
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

    private static void ReadAttribute(AttributeData attribute, LogCallSiteShape shape)
    {
        // The five constructors: (), (int, LogLevel, string), (LogLevel, string), (LogLevel), (string).
        foreach (var argument in attribute.ConstructorArguments)
        {
            switch (argument.Value)
            {
                case int id when argument.Type?.SpecialType == SpecialType.System_Int32:
                    shape.EventId = id;
                    break;
                case int level:
                    shape.Level = level;
                    break;
                case string message:
                    shape.Message = message;
                    break;
            }
        }

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "EventId" when named.Value.Value is int id:
                    shape.EventId = id;
                    break;
                case "EventName" when named.Value.Value is string name:
                    shape.EventName = name;
                    break;
                case "Level" when named.Value.Value is int level:
                    shape.Level = level;
                    break;
                case "Message" when named.Value.Value is string message:
                    shape.Message = message;
                    break;
                case "SkipEnabledCheck" when named.Value.Value is bool skip:
                    shape.SkipEnabledCheck = skip;
                    break;
            }
        }

        // LogLevel.None is the attribute's "not set": the level is a parameter then.
        if (shape.Level == LogLevelNone)
            shape.Level = null;
    }
}
