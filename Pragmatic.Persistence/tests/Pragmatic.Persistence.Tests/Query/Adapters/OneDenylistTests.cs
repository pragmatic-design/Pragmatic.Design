using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;
using Xunit;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     The grid denylist is <b>one</b> list, and this is the runtime end of it.
/// </summary>
/// <remarks>
///     <para>
///         Two paths ask the same question — may a client <em>name</em> this column in a filter or a
///         sort? — from different assemblies: the generator, deciding which fields the bridge and the
///         <c>[GridAdapter]</c> may switch on, and this policy, when a PrimeNG or DevExpress request
///         arrives. They were two hand-maintained copies, each with a comment saying so.
///     </para>
///     <para>
///         ⚠️ The arrangement is the defect, not the contents: the copies agreed on the day they were
///         written and nothing kept them agreeing. That shape had already become a real defect on the
///         wire-name list — a name stripped by one end and published by the other, both sides correct
///         on their own — which is why <c>ReservedWireNames</c> is a linked file, and why the grid list
///         is one now too.
///     </para>
///     <para>
///         Asserted through the adapter rather than by calling the list: it is internal, for the reason
///         the shared file gives (the same public type in two assemblies a test project references
///         together is <c>CS0433</c>). Going through the request is also the stronger claim — what a
///         client can name, not what a helper returns. The generator end of the same list is
///         <c>GridBridgeGeneratorTests.TheSameSensitiveNames_AreWithheldFromTheBridge</c>.
///     </para>
/// </remarks>
public class OneDenylistTests
{
    /// <summary>An entity that holds exactly what a client must not be able to interrogate.</summary>
    private sealed class Account
    {
        public Guid PersistenceId { get; set; }
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string AccessScopes { get; set; } = "";
        public string RowVersion { get; set; } = "";
    }

    private static IQueryable<Account> Accounts() =>
    new[]
    {
        new Account { Email = "a@x", PasswordHash = "hash-a", ApiKey = "k-a", OwnerId = "u1", TenantId = "t1" },
        new Account { Email = "b@x", PasswordHash = "hash-b", ApiKey = "k-b", OwnerId = "u2", TenantId = "t2" },
    }.AsQueryable();

    private static List<Account> FilteredBy(string field, object value, string matchMode = "startswith")
        => PersistenceQuery.For<Account>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent
            {
                Filters = new Dictionary<string, PrimeNGFilterMetadata>
                {
                    [field] = new() { Value = value, MatchMode = matchMode }
                }
            })
            .Build(Accounts())
            .ToList();

    /// <summary>A credential column cannot be named in a filter.</summary>
    /// <remarks>
    ///     This is the attack the list exists for: <c>startswith</c> over a hash turns the presence of
    ///     rows into an oracle that reads the value one character at a time. Two rows back means the
    ///     filter was refused; one would mean it answered.
    /// </remarks>
    [Theory]
    [InlineData("PasswordHash", "hash-a")]
    [InlineData("ApiKey", "k-a")]
    public void ACredentialColumn_CannotBeFiltered(string field, string probe)
        => FilteredBy(field, probe).Should().HaveCount(2,
            "the filter was refused: a narrowed result is the oracle this list exists to deny");

    /// <summary>Neither can the authorization shape.</summary>
    /// <remarks>
    ///     Not a credential, and worse in a different way: <c>OwnerId</c> and <c>AccessScopes</c> tell a
    ///     caller who holds a row and what makes it visible — reconnaissance for the next request.
    /// </remarks>
    [Theory]
    [InlineData("OwnerId", "u1")]
    [InlineData("TenantId", "t1")]
    [InlineData("AccessScopes", "scope")]
    [InlineData("PersistenceId", "1")]
    [InlineData("RowVersion", "v")]
    public void TheAuthorizationShape_CannotBeFiltered(string field, string probe)
        => FilteredBy(field, probe).Should().HaveCount(2);

    /// <summary>However the caller spells it.</summary>
    /// <remarks>
    ///     A grid client sends the column key it was given, and casing is not something the wire agreed
    ///     on: a denylist matching only <c>PasswordHash</c> is lifted by asking for <c>passwordhash</c>.
    /// </remarks>
    [Theory]
    [InlineData("passwordhash")]
    [InlineData("PASSWORDHASH")]
    [InlineData("ownerid")]
    public void TheRefusalIsCaseInsensitive(string field)
        => FilteredBy(field, "hash-a").Should().HaveCount(2);

    /// <summary>The control: an ordinary column still filters.</summary>
    /// <remarks>
    ///     Without it every assertion above is satisfied by an adapter that refuses everything — which
    ///     is an off switch, and looks exactly like a careful denylist.
    /// </remarks>
    [Fact]
    public void AnOrdinaryColumn_StillFilters()
        => FilteredBy("Email", "a@x", "equals").Should().ContainSingle()
            .Which.Email.Should().Be("a@x");

    /// <summary>And sorting is refused on the same names, because it leaks the same way.</summary>
    [Fact]
    public void ACredentialColumn_CannotBeSorted()
    {
        var sorted = PersistenceQuery.For<Account>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent { SortField = "PasswordHash", SortOrder = -1 })
            .Build(Accounts())
            .ToList();

        sorted.Select(a => a.Email).Should().Equal(["a@x", "b@x"],
            "the rows keep the order they had: ordering by a hash ranks rows by its value");
    }
}
