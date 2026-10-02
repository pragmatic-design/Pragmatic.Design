namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     A relation that is declared and generates no navigation, because it crosses a boundary.
/// </summary>
/// <param name="OtherEntity">The entity on the far side.</param>
/// <param name="OwnBoundary">The boundary the entity being mapped belongs to.</param>
/// <param name="OtherBoundary">The boundary the far side belongs to.</param>
/// <remarks>
///     Carried so a diagnostic can say why a property is absent. "Not found on this type" is true and
///     unhelpful here: nothing is misspelled, the relation exists, and the navigation was deliberately
///     not emitted because the two entities live in different <c>DbContext</c>s.
/// </remarks>
internal readonly record struct CrossBoundaryRelation(
    string OtherEntity,
    string OwnBoundary,
    string OtherBoundary);
