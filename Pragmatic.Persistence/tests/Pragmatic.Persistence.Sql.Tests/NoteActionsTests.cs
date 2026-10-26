using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Temporal.Clock;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The note actions and list query [HasNotes] generates for a note the Sql engine persists, on the five databases: through the child's
///     repository, the note's soft delete and its property's scopes and tenancy held by the engine's filters. anna holds desk-north, in acme:
///     RIVA is hers to annotate, ALPE (desk-north, another tenant) is not.
/// </summary>
[Collection("pragmatic databases")]
public class NoteActionsTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private sealed class Caller(SqlSession session, IQueryFilterToggle toggle, TestUser user) : IDisposable
    {
        public SqlUnitOfWork UnitOfWork { get; } = new(session, new FixedClock(UnitOfWorkTests.Now), user, new TenantScope());

        public PropertyNote.SqlRepository Notes => new(UnitOfWork);

        public TestUser User { get; } = user;

        public IServiceProvider Services { get; } = new ServiceCollection().AddSingleton(toggle).BuildServiceProvider();

        public IClock Clock { get; } = new TestInstant();

        public void Dispose() => UnitOfWork.Dispose();
    }

    /// <summary>The clock the actions stamp with: UnitOfWorkTests' instant.</summary>
    private sealed class TestInstant : IClock
    {
        public DateTimeOffset UtcNow => UnitOfWorkTests.Now;
        public DateTimeOffset Now => UnitOfWorkTests.Now;
        public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow.UtcDateTime);
        public DateOnly Today => UtcToday;
        public TimeOnly UtcTimeOfDay => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
        public TimeOnly TimeOfDay => UtcTimeOfDay;
        public TimeProvider GetTimeProvider() => new FixedClock(UnitOfWorkTests.Now);
    }

    private async Task<(SqlSession Session, Guid Riva, Guid Alpe)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var properties = await session.Query<Property>().ToListAsync();
            return (session, properties.Single(p => p.Name == "RIVA").Id, properties.Single(p => p.Name == "ALPE").Id);
        }
    }

    private static async Task<Result<Guid, IError>> Add(Caller caller, Guid property, string content)
    {
        var added = await new AddPropertyNoteAction.Invoker(caller.Notes, caller.Services, caller.User, caller.Clock, caller.Services)
            .InvokeAsync(new AddPropertyNoteAction { PropertyId = property, Content = content });
        await caller.UnitOfWork.SaveChangesAsync();
        return added;
    }

    private static Task<Result<PropertyNoteDto, IError>> Get(Caller caller, Guid property, Guid note)
        => new GetPropertyNoteAction.Invoker(caller.Notes, caller.Services, caller.User, caller.Clock, caller.Services)
            .InvokeAsync(new GetPropertyNoteAction { PropertyId = property, NoteId = note });

    private static async Task<VoidResult<IError>> Saved(Caller caller, Task<VoidResult<IError>> action)
    {
        var result = await action;
        await caller.UnitOfWork.SaveChangesAsync();
        return result;
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_note_is_added_read_edited_listed_and_deleted_through_the_childs_repository(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva, _) = await Seeded(dialect);
        using (session)
        using (var caller = new Caller(session, _toggle, new TestUser("anna")))
        {
            var id = (await Add(caller, riva, "first")).Value;
            var read = await Get(caller, riva, id);
            Assert.Equal(("first", "anna", id, riva), (read.Value!.Content, read.Value.AuthorId, read.Value.Id, read.Value.PropertyId));

            var edited = await Saved(caller, new UpdatePropertyNoteAction.Invoker(caller.Notes, caller.Services, caller.User, caller.Clock, caller.Services)
                .InvokeAsync(new UpdatePropertyNoteAction { PropertyId = riva, NoteId = id, Content = "edited" }));
            Assert.True(edited.IsSuccess);
            Assert.True((await Get(caller, riva, id)).Value!.IsEdited);

            using (var other = new Caller(session, _toggle, new TestUser("bruno")))
                Assert.IsType<ForbiddenError>((await new UpdatePropertyNoteAction.Invoker(other.Notes, other.Services, other.User, other.Clock, other.Services)
                    .InvokeAsync(new UpdatePropertyNoteAction { PropertyId = riva, NoteId = id, Content = "not his" })).Error);

            var services = new ServiceCollection().AddScoped<IReadRepository<PropertyNote>>(_ => new PropertyNote.SqlReadRepository(session)).BuildServiceProvider();
            var page = await new ListPropertyNotesQuery.Invoker(services).RunAsync(new ListPropertyNotesQuery { PropertyId = riva });
            Assert.Equal([("edited", id)], page.Value!.Items.Select(n => (n.Content, n.Id)));

            var deleted = await Saved(caller, new DeletePropertyNoteAction.Invoker(caller.Notes, caller.Services, caller.User, caller.Clock, caller.Services)
                .InvokeAsync(new DeletePropertyNoteAction { PropertyId = riva, NoteId = id }));
            Assert.True(deleted.IsSuccess);
            Assert.IsType<NotFoundError>((await Get(caller, riva, id)).Error);
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_note_on_another_tenants_property_is_out_of_reach(SqlDialect dialect)
    {
        var (session, _, alpe) = await Seeded(dialect);
        using (session)
        {
            Guid id;
            using (TenantScope.BeginScope("other"))
            using (var theirs = new Caller(session, _toggle, new TestUser("anna")))
                id = (await Add(theirs, alpe, "theirs")).Value;

            using var tenant = TenantScope.BeginScope("acme");
            using var caller = new Caller(session, _toggle, new TestUser("anna"));
            Assert.IsType<NotFoundError>((await Get(caller, alpe, id)).Error);
            Assert.IsType<NotFoundError>((await new DeletePropertyNoteAction.Invoker(caller.Notes, caller.Services, caller.User, caller.Clock, caller.Services)
                .InvokeAsync(new DeletePropertyNoteAction { PropertyId = alpe, NoteId = id })).Error);
        }
    }
}
