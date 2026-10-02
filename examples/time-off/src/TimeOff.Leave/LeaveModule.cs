using Pragmatic.Identity.Local.Actions;

namespace TimeOff.Leave;

/// <summary>
///     The module the host includes.
/// </summary>
/// <remarks>
///     The local identity package is imported into this boundary: its operations run against the
///     employees' credentials. Only what an employee does with an account HR already created is exposed —
///     signing in, changing the password, and the reset that also serves as the invitation. Registration
///     is not: accounts come with the employee. Signing in is the package's <c>SignInUser</c>, which
///     issues the token; what the token says about the employee is <c>EmployeeClaims</c>.
/// </remarks>
[Module(Name = "TimeOff.Leave", Version = "1.0.0",
    Description = "Leave requests: employees ask, managers decide for their team, HR administers")]
[UsePackage<LocalIdentityPackage, LeaveBoundary>]
// Anonymous by nature: it is how a caller gets the token everything else asks for.
[ExposeEndpoint<SignInUser>(HttpVerb.Post, "sign-in", AllowAnonymous = true)]
[ExposeEndpoint<ChangePassword>(HttpVerb.Post, "change-password")]
// Anonymous by nature: whoever uses them has no password yet, or has lost it. Neither says whether an
// account exists, and the token travels out of band, never in a response.
[ExposeEndpoint<RequestPasswordReset>(HttpVerb.Post, "reset-password/request", AllowAnonymous = true)]
[ExposeEndpoint<ConfirmPasswordReset>(HttpVerb.Post, "reset-password/confirm", AllowAnonymous = true)]
public sealed class LeaveModule;
