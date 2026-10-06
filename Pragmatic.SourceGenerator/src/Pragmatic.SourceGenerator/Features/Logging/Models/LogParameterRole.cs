namespace Pragmatic.SourceGenerator.Features.Logging.Models;

/// <summary>What a parameter of a call site is for.</summary>
internal enum LogParameterRole
{
    /// <summary>A structured property of the entry.</summary>
    Property,

    /// <summary>The logger the entry goes to.</summary>
    Logger,

    /// <summary>The entry's level, when the attribute does not set one.</summary>
    Level,

    /// <summary>The entry's exception, which is a property as well when the template names it.</summary>
    Exception,
}
