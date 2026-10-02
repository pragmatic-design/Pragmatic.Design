// ReSharper disable once CheckNamespace
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen;

/// <summary>
///     Shared logic for endpoint body DTO detection.
///     Used by both Endpoints SG and Validation SG to ensure consistent behavior.
/// </summary>
internal static class EndpointBodyDetection
{
    /// <summary>
    ///     Gets the generated body DTO name for an endpoint type.
    /// </summary>
    public static string GetBodyDtoName(string endpointTypeName) => $"{endpointTypeName}Body";

    /// <summary>
    ///     Determines whether a body DTO record should be generated.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         DomainActions and Mutations bind a single complex body property directly; a regular
    ///         endpoint wraps it, which is the published contract of the ones that already exist and
    ///         the reason <c>[Endpoint(BindBodyDirectly = true)]</c> is opt-in rather than the
    ///         default. Without it a PATCH could not accept a patch document at all: the body was
    ///         always a record with the document in a field.
    ///     </para>
    ///     <para>
    ///         Two or more properties always need the record, and so does a single scalar — a string
    ///         or a Guid cannot be a request body on its own.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     <c>httpMethod</c> is the verb, so a GET is not given a body DTO to validate.
    ///     ⚠️ <b><paramref name="httpMethod" /> is the same rule as
    ///     <c>EndpointModel.CarriesNoRequestBody</c>, restated because this copy of the decision exists
    ///     and is used by the validation transform.</b> The two must change together. If only the other
    ///     copy learns a rule — GET binding from the query string, say — the body DTO stops being
    ///     generated while its validator keeps being generated, referring to properties of a type that
    ///     does not exist. The framework gate stays green — its own application has no GET carrying
    ///     validated properties — and an application built on the packages fails to compile on generated
    ///     code it did not write.
    ///     <para>
    ///         Two copies of one decision is the defect; the parameter is the smaller repair. Merging
    ///         them means giving this shared helper the endpoint model, which it deliberately does not
    ///         take.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     ⚠️ <c>isQuery</c> is the newest half of this rule and the reason it is here rather than
    ///     copied: a declared <c>[Query]</c> never gets an envelope — its properties are filters and
    ///     paging, which travel in the query string, and the one thing it can take in the body, a
    ///     canonical grid request, is bound directly. A caller that predicts one emits a validator for a
    ///     <c>partial record {Query}Body</c> whose other half is never written, so every property it
    ///     names is <c>CS0103</c> in a file the author cannot edit.
    /// </remarks>
    /// <remarks>
    ///     ⚠️ <c>carriesMultipartRequest</c> is the fourth half of the same rule, and it is here for the
    ///     reason the others are: an operation that carries a file is <c>multipart/form-data</c>, so
    ///     there is no JSON body for a property to come from — every value travels as a form field.
    ///     Predicting an envelope for one produced a handler naming an undeclared <c>body</c>
    ///     (<c>CS0103</c>) and, from the other copy of this decision, a validator for a <c>…Body</c>
    ///     record nobody emits.
    /// </remarks>
    public static bool NeedsBodyDto(
        bool isDomainAction,
        bool isMutation,
        int bodyPropertyCount,
        bool hasSingleScalarBody = false,
        bool bindBodyDirectly = false,
        string? httpMethod = null,
        bool isQuery = false,
        bool carriesMultipartRequest = false)
        => bodyPropertyCount > 0 &&
           !isQuery &&
           !carriesMultipartRequest &&
           !CarriesNoRequestBody(httpMethod) &&
           ((!isDomainAction && !isMutation && !bindBodyDirectly)
            || bodyPropertyCount > 1
            || hasSingleScalarBody);

    /// <summary>Whether the verb carries no request body, so its values come from the query string.</summary>
    public static bool CarriesNoRequestBody(string? httpMethod)
        => httpMethod is not null
           && httpMethod.Equals("Get", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     The verb from <c>[Endpoint(HttpVerb.X, route)]</c>, as the name of the enum member.
    /// </summary>
    /// <param name="attributes">The attributes on the operation.</param>
    /// <param name="endpointAttributeFullName">The fully-qualified name of the Endpoint attribute.</param>
    /// <returns>The verb name, or null when the attribute carries none.</returns>
    /// <remarks>
    ///     Here rather than in the transform that needs it, for two reasons that agree. It reads an
    ///     attribute and decides nothing, which is what this class is for; and null from it means "no
    ///     verb declared", not "a declaration was discarded" — a distinction the silent-drops ratchet
    ///     cannot make from inside a transform file, where every `return null` reads as a rejection
    ///     nobody was told about.
    /// </remarks>
    public static string? GetHttpMethod(
        ImmutableArray<AttributeData> attributes, string endpointAttributeFullName)
    {
        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() != endpointAttributeFullName)
                continue;

            // The enum reaches the attribute as its underlying value, so the member is resolved from
            // the parameter's type rather than read off the argument.
            if (attr.ConstructorArguments.Length >= 1
                && attr.ConstructorArguments[0] is { Kind: TypedConstantKind.Enum, Value: not null } verb
                && verb.Type is INamedTypeSymbol enumType)
            {
                foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.HasConstantValue && Equals(member.ConstantValue, verb.Value))
                        return member.Name;
                }
            }
        }

        return null;
    }
}
