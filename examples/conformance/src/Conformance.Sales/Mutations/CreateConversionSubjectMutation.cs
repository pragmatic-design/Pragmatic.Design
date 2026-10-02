using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;

namespace Conformance.Sales.Mutations;

/// <summary>
///     Writes four scalars in the wrong types, on purpose.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: <em>automatic conversion applies to writes too</em>. Each property carries a
///         type different from the same-named property on the entity, and no <c>[MapConverter]</c> says
///         how to go from one to the other: if something reaches its destination, it is because the
///         generator worked out the conversion by itself.
///     </para>
///     <para>
///         ⚠️ What matters is <b>where</b> the conversion is proven: asserting on the <b>generated
///         text</b> cannot tell a right branch from one nobody takes at runtime. This case proves it on
///         the database.
///     </para>
///     <para>
///         ⚠️ <c>[AllowAnonymous]</c> for the same reason as the others: authorization has its own cell,
///         and bringing it in here would tie this case to a permission without proving authorization
///         any better than a dedicated test.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/conversion-subjects")]
[ReturnsDto<ConversionSubjectDto>]
public partial class CreateConversionSubjectMutation : Mutation<ConversionSubject>
{
    /// <summary>A string where the entity wants a <c>Guid</c>.</summary>
    public required string ExternalId { get; init; }

    /// <summary>A string where the entity wants a <c>bool</c>.</summary>
    public required string IsPriority { get; init; }

    /// <summary>A string where the entity wants an <c>int</c>.</summary>
    public required string Quantity { get; init; }

    /// <summary>And the opposite direction: a number where the entity wants a string.</summary>
    public required int Code { get; init; }
}
