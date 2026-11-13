using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     What a relation's <c>OnSoftDelete</c> cannot do (PRAG0640, PRAG0641).
/// </summary>
internal static partial class PersistenceDiagnostics
{
    /// <summary>PRAG0640: <c>OnSoftDelete = SetNull</c> on a key that cannot be null.</summary>
    /// <remarks>
    ///     The soft delete would have to write null into a required column. Refused at compile time rather than
    ///     at the first delete, when the save fails on a row the application thought it could empty.
    /// </remarks>
    public static readonly DiagnosticDescriptor SoftDeleteSetNullOnRequiredKey = DiagnosticFactory.Error(
        "PRAG0640",
        "OnSoftDelete = SetNull needs an optional key",
        "'{0}' empties its key to '{1}' when '{1}' is soft-deleted (OnSoftDelete = SetNull), but the key is "
        + "required — declare the relation with Required = false on '{0}', or choose Restrict or Cascade",
        "A soft delete that sets the referrers' key to null needs a key that can be null.");

    /// <summary>PRAG0641: <c>OnSoftDelete = Cascade</c> towards an entity that is not <c>[SoftDelete]</c>.</summary>
    /// <remarks>
    ///     The cascade soft-deletes the referrers with their principal. A referrer that is not soft-deletable has
    ///     no flag to raise, and deleting it for real would be the opposite of what a soft delete promises.
    /// </remarks>
    public static readonly DiagnosticDescriptor SoftDeleteCascadeToHardEntity = DiagnosticFactory.Error(
        "PRAG0641",
        "OnSoftDelete = Cascade needs a soft-deletable referrer",
        "'{0}' is soft-deleted with '{1}' (OnSoftDelete = Cascade), but '{0}' is not [SoftDelete] — mark it "
        + "[SoftDelete], or choose Restrict or SetNull",
        "A soft delete cascades by soft-deleting the referrers, which needs them to be soft-deletable.");
}
