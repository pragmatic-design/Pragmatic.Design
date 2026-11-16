namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>The three things <c>IncludeEmpty</c> refuses (#766), one diagnostic each.</summary>
internal enum EmptyGroupRefusalKind
{
    /// <summary>PRAG0748: more than one key, whose values multiply or are not known.</summary>
    CompositeKey,

    /// <summary>PRAG0749: a key that is not an enum, or not projected onto the view, so its values are not known or not settable.</summary>
    KeyNotAProjectedEnum,

    /// <summary>PRAG0750: a member with no value for an empty group (Min, Max, Avg, a non-key From) that is not nullable.</summary>
    NoValueNotNullable
}
