namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     A readable member of the <c>[PragmaticUser]</c> entity, as <c>[FromCurrentUser(member)]</c> may name it.
/// </summary>
/// <param name="Name">The member's name.</param>
/// <param name="TypeFullName">
///     Its type, fully qualified and without a nullable reference annotation — the form the query
///     property's type is compared in.
/// </param>
internal sealed record UserMemberModel(string Name, string TypeFullName);
