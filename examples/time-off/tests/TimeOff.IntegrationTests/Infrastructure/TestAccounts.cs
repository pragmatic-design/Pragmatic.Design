namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     The accounts the tests sign in with.
/// </summary>
public static class TestAccounts
{
    /// <summary>
    ///     Created by the host at startup from configuration: the account HR starts from. Shared by every
    ///     test of the collection, so no test changes its password.
    /// </summary>
    public static readonly TestAccount FirstAdministrator =
        new("Hannah Reyes", "hr.admin@time-off.test", "First-Admin-Pa55!");
}
