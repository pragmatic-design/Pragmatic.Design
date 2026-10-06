// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>Why a <c>[LoggerMessage]</c> method cannot get a generated body, or what is wrong with it.</summary>
internal enum LogCallSiteProblemKind
{
    /// <summary>Not a <c>partial void</c> definition, generic, or with a <c>ref</c>/<c>out</c>/<c>in</c> parameter.</summary>
    WrongShape,

    /// <summary>A placeholder names no parameter.</summary>
    PlaceholderWithoutParameter,

    /// <summary>A parameter no placeholder names; still a structured property.</summary>
    ParameterNotInTemplate,

    /// <summary>No logger: a static method takes none, or an instance one finds none or more than one.</summary>
    NoLogger,

    /// <summary>The attribute sets no level and no parameter is a <c>LogLevel</c>.</summary>
    NoLevel,

    /// <summary>The template does not parse, or uses an alignment.</summary>
    MalformedTemplate,

    /// <summary>A type the method is declared in is not <c>partial</c>.</summary>
    ContainerNotPartial,
}
