namespace Pragmatic.Testing;

/// <summary>
///     What a generated contract test hands the application before it sends: whose contract it is, which
///     operation, and the request itself.
/// </summary>
/// <param name="Boundary">
///     The boundary the generated test class belongs to — <c>Intake</c>, <c>Verify</c>. An application
///     made of more than one service needs it to answer "which host, which signing key, which role
///     table", and it is the one thing the request itself does not carry: a path is a routing table
///     somebody has to keep in step with the routes.
/// </param>
/// <param name="Operation">
///     The generated test's own name for the operation — <c>CreateWorkspaceMutation</c>,
///     <c>DownloadCaseDocument</c>. ⚠️ It is the endpoint's name and not the caller's: both halves of an
///     authorization pair carry the same one, so a fixture that decided who the caller is by reading this
///     gives the privileged token to the caller that has to be refused.
/// </param>
/// <param name="Message">The request, to add to or replace anything on.</param>
public sealed record ContractRequest(string Boundary, string Operation, HttpRequestMessage Message);
