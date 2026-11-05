using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     <c>TimeOnly</c>, <c>DateOnly</c> and <c>TimeSpan</c> members of an entity the documents engine persists (#476): written and read back, an
///     optional one empty until written; filtered and sorted as the values compare in .NET, across a month, a year and a negative duration; and
///     a declared query's <c>[MapProperty]</c> projection over them. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class TimeOfDayTests(DocumentServers servers)
{
    /// <summary>
    ///     Three stays of one batch, chosen so that a wrong stored form orders them differently: arrivals across a month's and a year's end (a day
    ///     first or a month first would not sort them), check-ins whose hour has one digit, a negative length (text would sort it last).
    /// </summary>
    private static async Task<(DocSession Session, string Batch, List<Stay> Stays)> StaysAsync(DocumentServers servers, Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Stay>();
        var batch = "B-" + Guid.NewGuid().ToString("N")[..10];
        var stays = new List<Stay>
        {
            new() { Batch = batch, Code = "early", CheckIn = new TimeOnly(9, 30), Arrival = new DateOnly(2026, 12, 31), Length = TimeSpan.FromHours(26) },
            new() { Batch = batch, Code = "noon", CheckIn = new TimeOnly(14, 0), Arrival = new DateOnly(2027, 1, 2), Length = TimeSpan.FromHours(-1), LateCheckOut = new TimeOnly(13, 0) },
            new() { Batch = batch, Code = "late", CheckIn = new TimeOnly(23, 15, 30, 500), Arrival = new DateOnly(2026, 2, 28), Length = TimeSpan.FromMinutes(90), Departure = new DateOnly(2026, 3, 1), Grace = TimeSpan.FromMinutes(15) },
        };
        await session.InsertManyAsync(stays);
        return (session, batch, stays);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_time_of_day_a_day_and_a_duration_are_read_back(Store store)
    {
        var (session, _, stays) = await StaysAsync(servers, store);
        var early = stays[0];

        var loaded = await new Stay.DocRepository(UnitOfWorkTests.UnitOfWork(session)).GetByIdAsync(early.Id);
        Assert.NotNull(loaded);
        Assert.Equal(new TimeOnly(9, 30), loaded.CheckIn);
        Assert.Equal(new DateOnly(2026, 12, 31), loaded.Arrival);
        Assert.Equal(TimeSpan.FromHours(26), loaded.Length);
        Assert.Null(loaded.LateCheckOut);
        Assert.Null(loaded.Departure);
        Assert.Null(loaded.Grace);

        // The optional ones, written later, through a replace; the time of day to its last tick.
        loaded.LateCheckOut = new TimeOnly(12, 59, 59, 999, 999);
        loaded.Departure = new DateOnly(2027, 1, 1);
        loaded.Grace = TimeSpan.FromTicks(-1);
        await session.ReplaceAsync(loaded);

        var read = Assert.Single(await session.Query<Stay>().Where((s, id) => s.Id == id, early.Id).ToListAsync());
        Assert.Equal(new TimeOnly(12, 59, 59, 999, 999), read.LateCheckOut);
        Assert.Equal(new DateOnly(2027, 1, 1), read.Departure);
        Assert.Equal(TimeSpan.FromTicks(-1), read.Grace);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task They_filter_as_the_values_compare(Store store)
    {
        var (session, batch, _) = await StaysAsync(servers, store);

        Assert.Equal(["late", "noon"], Codes(await session.Query<Stay>().Where((s, a) => s.Batch == a.Batch && s.CheckIn >= a.From, (Batch: batch, From: new TimeOnly(14, 0))).ToListAsync()));
        Assert.Equal(["early", "late"], Codes(await session.Query<Stay>().Where((s, a) => s.Batch == a.Batch && s.Arrival < a.Before, (Batch: batch, Before: new DateOnly(2027, 1, 1))).ToListAsync()));
        Assert.Equal(["noon"], Codes(await session.Query<Stay>().Where((s, a) => s.Batch == a.Batch && s.Length < a.Under, (Batch: batch, Under: TimeSpan.Zero)).ToListAsync()));
        Assert.Equal(["noon"], Codes(await session.Query<Stay>().Where((s, a) => s.Batch == a.Batch && s.LateCheckOut == a.At, (Batch: batch, At: (TimeOnly?)new TimeOnly(13, 0))).ToListAsync()));
        Assert.Equal(["early", "noon"], Codes(await session.Query<Stay>().Where((s, b) => s.Batch == b && s.Departure == null, batch).ToListAsync()));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task They_sort_as_the_values_compare(Store store)
    {
        var (session, batch, _) = await StaysAsync(servers, store);

        Assert.Equal(["late", "early", "noon"], (await session.Query<Stay>().Where((s, b) => s.Batch == b, batch).OrderBy(s => s.Arrival).ToListAsync()).Select(s => s.Code));
        Assert.Equal(["early", "noon", "late"], (await session.Query<Stay>().Where((s, b) => s.Batch == b, batch).OrderBy(s => s.CheckIn).ToListAsync()).Select(s => s.Code));
        Assert.Equal(["noon", "late", "early"], (await session.Query<Stay>().Where((s, b) => s.Batch == b, batch).OrderBy(s => s.Length).ToListAsync()).Select(s => s.Code));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_declared_query_projects_its_mapped_properties(Store store)
    {
        var (session, batch, _) = await StaysAsync(servers, store);
        IReadRepository<Stay> repository = new Stay.DocReadRepository(session);

        var cards = await repository.RunAsync(new StayCardsQuery { Batch = batch, ArrivalSort = SortDirection.Ascending });

        Assert.Equal(["late", "early", "noon"], cards.Select(c => c.Label));
        Assert.Equal([new DateOnly(2026, 2, 28), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 2)], cards.Select(c => c.ArrivesOn));
    }

    /// <summary>
    ///     A declared projection with a formatted <c>[MapProperty]</c> and a <c>[MapConverter]</c> member (#537): the source fields read, the
    ///     mapping's format and converter applied after the read, as the generated mapping applies them.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_declared_projection_formats_and_converts_its_members(Store store)
    {
        var (session, batch, stays) = await StaysAsync(servers, store);
        IReadRepository<Stay> repository = new Stay.DocReadRepository(session);

        var labels = (await repository.RunAsync(new StayLabelsQuery { Batch = batch })).OrderBy(l => l.Code, StringComparer.Ordinal).ToList();

        Assert.Equal([("early", "31/12/2026", "26h"), ("late", "28/02/2026", "1.5h"), ("noon", "02/01/2027", "-1h")], labels.Select(l => (l.Code, l.ArrivesOn, l.Lasts)));
        Assert.Equal(StayLabel.FromEntity(stays[0]).ArrivesOn, labels[0].ArrivesOn); // what the generated mapping writes for the same stay
    }

    /// <summary>The stays' codes in name order: a filter's result, whatever order the server returns it in.</summary>
    private static List<string> Codes(List<Stay> stays) => stays.Select(s => s.Code).Order(StringComparer.Ordinal).ToList();
}
