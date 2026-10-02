using Conformance.Split.Entities;
using Pragmatic.Actions.Attributes;

namespace Conformance.Split;

/// <summary>
///     The second boundary, in the same assembly as the first.
/// </summary>
/// <remarks>
///     It is what makes the other's declaration necessary, and vice versa: two boundaries, and then
///     «whose entity is this?» becomes a question with two possible answers. The rest of the repository
///     does not have this shape — every other topology puts one boundary per assembly — and that is why
///     this library exists.
/// </remarks>
[Boundary]
[Owns<Journal>]
public partial class JournalBoundary;
