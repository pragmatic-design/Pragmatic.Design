using Pragmatic.SourceGen;

namespace Pragmatic.Testing.Comparers.SourceGenerator.Models;

/// <summary>One <c>[GenerateComparer&lt;T&gt;]</c> declaration, resolved into what the template needs.</summary>
internal sealed record ComparerModel
{
    /// <summary>The fully-qualified compared type, <c>global::</c>-prefixed.</summary>
    public required string TypeFullName { get; init; }

    /// <summary>The type's simple name, used in the generated class name and in messages.</summary>
    public required string TypeShortName { get; init; }

    /// <summary>The generated class name — <c>OrderDto</c> becomes <c>OrderDtoComparer</c>.</summary>
    public required string ClassName { get; init; }

    /// <summary>The members to compare, in declaration order.</summary>
    public required EquatableArray<ComparedMemberModel> Members { get; init; }
}
