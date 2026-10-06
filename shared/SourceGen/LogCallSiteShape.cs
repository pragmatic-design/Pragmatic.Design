using System.Collections.Generic;
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>What a <c>[LoggerMessage]</c> method declares, read once for the generator and the analyzer.</summary>
/// <remarks>
///     Holds symbols, so it never enters an incremental pipeline: the generator's transform turns it into
///     its value-equatable model straight away.
/// </remarks>
internal sealed class LogCallSiteShape
{
    public int EventId { get; set; } = -1;
    public string? EventName { get; set; }

    /// <summary>The attribute's level as the <c>LogLevel</c> value; null when the method takes it as a parameter.</summary>
    public int? Level { get; set; }

    public string Message { get; set; } = "";
    public bool SkipEnabledCheck { get; set; }

    /// <summary>The template's parts; null when it does not parse.</summary>
    public List<LogTemplateSegment>? Segments { get; set; }

    public IParameterSymbol? LoggerParameter { get; set; }

    /// <summary>For an instance method without a logger parameter: <c>this.{member}</c> or a primary-constructor parameter.</summary>
    public string? LoggerMember { get; set; }

    public IParameterSymbol? LevelParameter { get; set; }
    public IParameterSymbol? ExceptionParameter { get; set; }

    public List<LogCallSiteProblem> Problems { get; } = [];

    /// <summary>Whether a body can be generated.</summary>
    public bool IsGeneratable
    {
        get
        {
            foreach (var problem in Problems)
                if (problem.IsBlocking)
                    return false;
            return true;
        }
    }
}
