using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Comments;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Temporal.Clock;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The comment actions [HasComments] generates for a comment the Sql engine persists, on the five databases: they read and write through the
///     child's repository, and the engine's filters hold the comment's rules (moderation, internal notes) and its parent's visibility. The review is
///     anna's at RIVA; its comments are moderated and may be internal.
/// </summary>
[Collection("pragmatic databases")]
public class CommentActionsTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private sealed class Caller(SqlSession session, IQueryFilterToggle toggle, TestUser user) : IDisposable
    {
        public SqlUnitOfWork UnitOfWork { get; } = new(session, new FixedClock(UnitOfWorkTests.Now), user, new TenantScope());

        public ReviewComment.SqlRepository Comments => new(UnitOfWork);

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

    private async Task<(SqlSession Session, Guid Review)> Seeded(SqlDialect dialect, TestUser user)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(user, new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, (await session.Query<Review>().Where(r => r.Text == "anna@RIVA").ToListAsync()).Single().Id);
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
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_comment_is_added_moderated_read_edited_and_deleted_through_the_childs_repository(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var anna = new TestUser("anna");
        var (session, review) = await Seeded(dialect, anna);
        using (session)
        using (var caller = new Caller(session, _toggle, anna))
        {
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
                Assert.True((await session.Query<ReviewComment>().Where(c => c.Content == "edited").ToListAsync()).Single().IsDeleted); // soft deleted
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Only_the_author_edits_a_comment_and_an_internal_one_needs_view_internal(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var anna = new TestUser("anna");
        var (session, review) = await Seeded(dialect, anna);
        using (session)
        using (var caller = new Caller(session, _toggle, anna))
        {
            Assert.IsType<ForbiddenError>((await Add(caller, review, "staff only", CommentVisibility.Internal)).Error);

            var id = (await Add(caller, review, "mine")).Value;
            using (_toggle.UseMode(FilterMode.Raw))
                await new ModerateReviewCommentAction.Invoker(caller.Comments, caller.User, caller.Clock, caller.Services, caller.Services)
                    .InvokeAsync(new ModerateReviewCommentAction { ReviewId = review, CommentId = id, NewStatus = CommentStatus.Visible });
            await caller.UnitOfWork.SaveChangesAsync();

            using var other = new Caller(session, _toggle, new TestUser("bruno"));
            var edited = await new UpdateReviewCommentAction.Invoker(other.Comments, other.User, other.Clock, other.Services, other.Services)
                .InvokeAsync(new UpdateReviewCommentAction { ReviewId = review, CommentId = id, Content = "not his" });
            Assert.IsType<ForbiddenError>(edited.Error);
        }
    }
}
