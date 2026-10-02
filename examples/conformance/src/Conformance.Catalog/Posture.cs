using Pragmatic.Authorization;

// The auto-derivation posture, on for this module alone: every operation that names no permission and
// does not opt out with [AllowAnonymous] requires {boundary}.{operation}. Sales stays off, because its
// operations are anonymous by design — authorization is a cell of its own in the conformance matrix,
// and this module is where that cell is measured (TheDerivedPermissionOnARead).
[assembly: PragmaticAutoDerivePermissions]
