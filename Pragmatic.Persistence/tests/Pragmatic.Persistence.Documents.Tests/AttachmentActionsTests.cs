using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Storage.InMemory;
using Pragmatic.Temporal.Clock;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The attachment actions, list query and purge job [HasAttachments] generates for attachments the documents engine persists, on MongoDB and
///     Cosmos DB, as on the Sql engine (<c>AttachmentActionsTests</c> there): through the child's repository, the files in storage, the per-entity
///     limit (two) counted before the write, the soft delete keeping the file and the purge reclaiming it. anna, in acme, holds desk-north and the
///     property's view-all: RIVA and MARE are hers.
/// </summary>
[Collection("document servers")]
public class AttachmentActionsTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private readonly InMemoryFileStorage _storage = new();

    private static readonly string PropertyViewAll = new PropertyNote.ParentVisibilityFilter(new TestScopes("desk-north"), new TestUser("x")).BypassPermission;

    private static TestUser Anna => new("anna", [PropertyViewAll]);

    private sealed class Caller(DocSession session, IQueryFilterToggle toggle, TestUser user, InMemoryFileStorage storage)
    {
        public DocUnitOfWork UnitOfWork { get; } = new(session, new FixedClock(UnitOfWorkTests.Now), user, new TenantScope());

        public PropertyAttachment.DocRepository Attachments => new(UnitOfWork);

        public IServiceProvider Services { get; } = new ServiceCollection().AddSingleton(toggle).BuildServiceProvider();

        public IClock Clock { get; } = new TestInstant(UnitOfWorkTests.Now);

        public Task<Result<Guid, IError>> Upload(Guid property, string fileName)
            => new UploadPropertyAttachmentAction.Invoker(Attachments, UnitOfWork, user, Clock, storage, Services)
                .InvokeAsync(new UploadPropertyAttachmentAction
                {
                    PropertyId = property, FileContent = new MemoryStream("%PDF-1.7"u8.ToArray()), FileName = fileName, FileSize = 8, ContentType = "application/pdf",
                });

        public Task<Result<PropertyAttachmentDto, IError>> Get(Guid property, Guid attachment)
            => new GetPropertyAttachmentAction.Invoker(Attachments, Services).InvokeAsync(new GetPropertyAttachmentAction { PropertyId = property, AttachmentId = attachment });

        public async Task<VoidResult<IError>> Delete(Guid property, Guid attachment)
        {
            var deleted = await new DeletePropertyAttachmentAction.Invoker(Attachments, user, Clock, Services)
                .InvokeAsync(new DeletePropertyAttachmentAction { PropertyId = property, AttachmentId = attachment });
            await UnitOfWork.SaveChangesAsync();
            return deleted;
        }
    }

    private sealed class TestInstant(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
        public DateTimeOffset Now => now;
        public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow.UtcDateTime);
        public DateOnly Today => UtcToday;
        public TimeOnly UtcTimeOfDay => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
        public TimeOnly TimeOfDay => UtcTimeOfDay;
        public TimeProvider GetTimeProvider() => new FixedClock(now);
    }

    private async Task<(DocSession Session, Guid Riva, Guid Mare)> Seeded(Store store)
    {
        var seeded = await DocumentRows.SeededAsync(servers, store);
        if (store == Store.Cosmos)
            await seeded.EnsureContainerAsync<PropertyAttachment>();

        var session = DocumentServers.As(seeded, new PragmaticDocFilters(Anna, new TenantScope(), new TestScopes("desk-north"), _toggle));
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var properties = await session.Query<Property>().ToListAsync();
            return (session, properties.Single(p => p.Name == "RIVA").Id, properties.Single(p => p.Name == "MARE").Id);
        }
    }

    private static async Task<List<string>> Listed(DocSession session, Guid property)
    {
        var services = new ServiceCollection().AddScoped<IReadRepository<PropertyAttachment>>(_ => new PropertyAttachment.DocReadRepository(session)).BuildServiceProvider();
        var page = await new ListPropertyAttachmentsQuery.Invoker(services).RunAsync(new ListPropertyAttachmentsQuery { PropertyId = property });
        return page.Value!.Items.Select(a => a.FileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private async Task<int> Rows(DocSession session)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return (await session.Query<PropertyAttachment>().ToListAsync()).Count;
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_file_is_uploaded_read_listed_limited_and_soft_deleted(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva, mare) = await Seeded(store);
        var caller = new Caller(session, _toggle, Anna, _storage);

        var lease = (await caller.Upload(riva, "lease.pdf")).Value;
        await caller.Upload(riva, "plan.pdf");
        Assert.Equal(2, _storage.Count);

        var read = (await caller.Get(riva, lease)).Value!;
        Assert.Equal(("lease.pdf", 8L, "application/pdf", "anna", false), (read.FileName, read.FileSize, read.ContentType, read.UploadedBy, read.HasThumbnail));
        Assert.IsType<NotFoundError>((await caller.Get(mare, lease)).Error); // another property's route
        Assert.Equal(["lease.pdf", "plan.pdf"], await Listed(session, riva));

        Assert.IsType<ForbiddenError>((await caller.Upload(riva, "third.pdf")).Error); // two at most
        Assert.Equal(2, _storage.Count);

        Assert.True((await caller.Delete(riva, lease)).IsSuccess);
        Assert.IsType<NotFoundError>((await caller.Get(riva, lease)).Error);
        Assert.Equal(["plan.pdf"], await Listed(session, riva));
        Assert.Equal(2, _storage.Count); // a soft delete keeps the file
        Assert.Equal(2, await Rows(session));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_purge_removes_the_expired_rows_and_their_files(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva, mare) = await Seeded(store);
        var caller = new Caller(session, _toggle, Anna, _storage);
        await caller.Delete(riva, (await caller.Upload(riva, "old.pdf")).Value);
        await caller.Upload(mare, "kept.pdf");

        await Purge(session, UnitOfWorkTests.Now.AddDays(10)); // inside the 30 days: kept
        Assert.Equal((2, 2), (await Rows(session), _storage.Count));

        await Purge(session, UnitOfWorkTests.Now.AddDays(31));
        Assert.Equal((1, 1), (await Rows(session), _storage.Count));
        Assert.Equal(["kept.pdf"], await Listed(session, mare));
    }

    private async Task Purge(DocSession session, DateTimeOffset at)
    {
        var unitOfWork = new DocUnitOfWork(session, new FixedClock(at), new TestUser("purge"), new TenantScope());
        var job = new PurgePropertyAttachmentsJob(new PropertyAttachment.DocRepository(unitOfWork), unitOfWork, _toggle, _storage, new TestInstant(at),
            NullLogger<PurgePropertyAttachmentsJob>.Instance);
        await job.ExecuteAsync(new JobContext(Guid.NewGuid(), nameof(PurgePropertyAttachmentsJob), at, 0, 1), CancellationToken.None);
    }
}
