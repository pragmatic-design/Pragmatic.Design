using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Comments;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Temporal.Clock;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The comment actions [HasComments] generates for a comment the documents engine persists, on MongoDB and Cosmos DB, as on the Sql engine
///     (<c>CommentActionsTests</c> there): through the child's repository, the comment's rules held by the engine's filters. The review is anna's at
///     RIVA; its comments are moderated and may be internal.
/// </summary>
[Collection("document servers")]
public class CommentActionsTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private sealed class Caller(DocSession session, IQueryFilterToggle toggle, TestUser user)
    {
        public DocUnitOfWork UnitOfWork { get; } = new(session, new FixedClock(UnitOfWorkTests.Now), user, new TenantScope());

        public ReviewComment.DocRepository Comments => new(UnitOfWork);

        public TestUser User { get; } = user;

        public IServiceProvider Services { get; } = new ServiceCollection().AddSingleton(toggle).BuildServiceProvider();

        public IClock Clock { get; } = new TestInstant();
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

    private async Task<(DocSession Session, Guid Review)> Seeded(Store store)
    {
        var seeded = await DocumentRows.SeededAsync(servers, store);
        if (store == Store.Cosmos)
            await seeded.EnsureContainerAsync<ReviewComment>();
        var session = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, (await session.Query<Review>().ToListAsync()).Single(r => r.Text == "anna@RIVA").Id);
    }

    private static async Task<Result<Guid, IError>> Add(Caller caller, Guid review, string content, CommentVisibility visibility = CommentVisibility.Public)
    {
        var added = await new AddReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
            .InvokeAsync(new AddReviewCommentAction { ReviewId = review, Content = content, Visibility = visibility });
        await caller.UnitOfWork.SaveChangesAsync();
        return added;
    }

    private static Task<Result<ReviewCommentDto, IError>> Get(Caller caller, Guid review, Guid comment)
        => new GetReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
            .InvokeAsync(new GetReviewCommentAction { ReviewId = review, CommentId = comment });

    private static async Task<VoidResult<IError>> Saved(Caller caller, Task<VoidResult<IError>> action)
    {
        var result = await action;
        await caller.UnitOfWork.SaveChangesAsync();
        return result;
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_comment_is_added_moderated_read_edited_and_deleted_through_the_childs_repository(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, review) = await Seeded(store);
        var caller = new Caller(session, _toggle, new TestUser("anna"));

        var added = await Add(caller, review, "first");
        Assert.True(added.IsSuccess);
        var id = added.Value;

        // Pending: out of a reader's read, in the moderation queue.
        Assert.IsType<NotFoundError>((await Get(caller, review, id)).Error);
        var queue = await new ListPendingReviewCommentsAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
            .InvokeAsync(new ListPendingReviewCommentsAction { ReviewId = review });
        Assert.Equal(["first"], queue.Value!.Select(c => c.Content));

        var approved = await Saved(caller, new ModerateReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
            .InvokeAsync(new ModerateReviewCommentAction { ReviewId = review, CommentId = id, NewStatus = CommentStatus.Visible }));
        Assert.True(approved.IsSuccess);
        var read = await Get(caller, review, id);
        Assert.Equal(("first", "anna", id, review), (read.Value!.Content, read.Value.AuthorId, read.Value.Id, read.Value.ReviewId));

        var edited = await Saved(caller, new UpdateReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
            .InvokeAsync(new UpdateReviewCommentAction { ReviewId = review, CommentId = id, Content = "edited" }));
        Assert.True(edited.IsSuccess);
        Assert.Equal("edited", (await Get(caller, review, id)).Value!.Content);

        // Another review's id is no way to the comment.
        Assert.IsType<NotFoundError>((await Get(caller, Guid.NewGuid(), id)).Error);

        var deleted = await Saved(caller, new DeleteReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
            .InvokeAsync(new DeleteReviewCommentAction { ReviewId = review, CommentId = id }));
        Assert.True(deleted.IsSuccess);
        Assert.IsType<NotFoundError>((await Get(caller, review, id)).Error);
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.True((await session.Query<ReviewComment>().ToListAsync()).Single(c => c.Content == "edited").IsDeleted); // soft deleted
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Only_the_author_edits_a_comment_and_an_internal_one_needs_view_internal(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, review) = await Seeded(store);
        var caller = new Caller(session, _toggle, new TestUser("anna"));
        Assert.IsType<ForbiddenError>((await Add(caller, review, "staff only", CommentVisibility.Internal)).Error);

        var id = (await Add(caller, review, "mine")).Value;
        using (_toggle.UseMode(FilterMode.Raw))
            await new ModerateReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
                .InvokeAsync(new ModerateReviewCommentAction { ReviewId = review, CommentId = id, NewStatus = CommentStatus.Visible });
        await caller.UnitOfWork.SaveChangesAsync();

        var other = new Caller(session, _toggle, new TestUser("bruno"));
        var edited = await new UpdateReviewCommentAction.Invoker(other.Comments, other.User, other.Clock, other.Services, other.Services)
            .InvokeAsync(new UpdateReviewCommentAction { ReviewId = review, CommentId = id, Content = "not his" });
        Assert.IsType<ForbiddenError>(edited.Error);
    }
}
