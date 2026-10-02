using System.Collections.Generic;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// Options declared on the trait attributes, verified through the REAL pipeline
/// (attribute → transform → model → template) instead of a hand-built model.
///
/// Hand-built models can express things the consumer cannot: that is exactly how
/// <c>EditWindowMinutes</c> stayed <c>int?</c> — unusable as an attribute argument — while a
/// snapshot test happily rendered the edit-window branch from a model set in C#.
/// </summary>
public class TraitOptionsFromSourceTests
{
    private const string CommentEntity = """
        using Pragmatic.Comments;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Blog.Articles
        {
            public sealed class BlogBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<BlogBoundary>]
            [Resource("articles")]
            [HasComments(__OPTIONS__)]
            public partial class Article : IEntity
            {
                public System.Guid Id { get; set; }
                public System.Guid PersistenceId { get => Id; set => Id = value; }
                public string Title { get; set; } = string.Empty;
            }

            [PragmaticDbContext("Blog")]
            public partial class BlogDbContext { }
        }
        """;

    private const string NoteEntity = """
        using Pragmatic.Notes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Support.Tickets
        {
            public sealed class SupportBoundary { }

            public sealed class Unrelated { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<SupportBoundary>]
            [Resource("tickets")]
            [HasNotes(__OPTIONS__)]
            public partial class Ticket : IEntity
            {
                public System.Guid Id { get; set; }
                public System.Guid PersistenceId { get => Id; set => Id = value; }
                public string Subject { get; set; } = string.Empty;
            }

            [PragmaticDbContext("Support")]
            public partial class SupportDbContext { }
        }
        """;

    private static string Comments(string options) => CommentEntity.Replace("__OPTIONS__", options);

    private static string Notes(string parent, string options = "")
        => NoteEntity.Replace("__PARENT__", parent).Replace("__OPTIONS__", options);

    /// <summary>
    /// One action spreads over several generated files (the class, its invoker, the endpoint);
    /// assert on all of them at once rather than guessing which hint name holds what.
    /// </summary>
    private static string Concat(IReadOnlyDictionary<string, string> sources, string hintFragment)
        => string.Join("\n", sources.Where(s => s.Key.Contains(hintFragment)).Select(s => s.Value));

    // ── EditWindowMinutes ────────────────────────────────────────────────

    [Fact]
    public void CommentEditWindow_SetOnAttribute_ReachesTheUpdateAction()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Comments("EditWindowMinutes = 45"));

        var update = Concat(sources, "UpdateArticleCommentAction");
        update.Should().Contain("AddMinutes(45)",
            "the edit window declared on the attribute must reach the generated Update action");
    }

    [Fact]
    public void CommentEditWindow_DefaultMinusOne_EmitsNoWindowCheck()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Comments("MaxLength = 500"));

        var update = Concat(sources, "UpdateArticleCommentAction");
        update.Should().NotContain("AddMinutes", "-1 means no limit, so no deadline is computed");
    }

    // ── AllowEditing ─────────────────────────────────────────────────────

    [Fact]
    public void CommentAllowEditingFalse_GeneratesNoUpdateActionOrEndpoint()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Comments("AllowEditing = false"));

        sources.Keys.Should().NotContain(k => k.Contains("UpdateArticleCommentAction"),
            "AllowEditing = false must make editing unreachable, not merely discouraged");

        var all = string.Join("\n", sources.Values);
        all.Should().NotContain("UpdateArticleCommentAction",
            "no endpoint, invoker or registration may reference an action that is not generated");
    }

    [Fact]
    public void CommentAllowEditingFalse_OmitsTheUpdatePermissionConstant()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Comments("AllowEditing = false"));

        var permissions = sources.Single(s => s.Key.Contains("CommentPermissions")).Value;
        permissions.Should().NotContain("string Update",
            "a grantable permission that gates nothing is worse than no permission");
        permissions.Should().Contain("string Delete", "the other operations stay");
    }

    [Fact]
    public void CommentAllowEditingDefault_StillGeneratesTheUpdateAction()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Comments("MaxLength = 500"));

        sources.Keys.Should().Contain(k => k.Contains("UpdateArticleCommentAction"));
        sources.Single(s => s.Key.Contains("CommentPermissions")).Value
            .Should().Contain("string Update");
    }

    [Fact]
    public void NoteAllowEditingFalse_GeneratesNoUpdateActionOrEndpoint()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Notes("Ticket", "AllowEditing = false"));

        var all = string.Join("\n", sources.Values);
        all.Should().NotContain("UpdateTicketNoteAction",
            "AllowEditing = false must make editing unreachable for notes too");

        sources.Single(s => s.Key.Contains("NotePermissions")).Value
            .Should().NotContain("string Update");
    }

    [Fact]
    public void NoteAllowEditingDefault_StillGeneratesTheUpdateAction()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Notes("Ticket"));

        sources.Keys.Should().Contain(k => k.Contains("UpdateTicketNoteAction"));
    }


    // ── AllowAnonymous is gone ───────────────────────────────────────────

    [Fact]
    public void CommentAuthorId_IsAlwaysTakenFromCurrentUser()
    {
        // The removed AllowAnonymous promised a null AuthorId; nothing ever honoured it.
        var (sources, _) = TraitCompilationHarness.Generate(Comments("MaxLength = 500"));

        Concat(sources, "AddArticleCommentAction")
            .Should().Contain("AuthorId = _currentUser.Id");
    }
}
