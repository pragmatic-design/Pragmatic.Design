using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Template unit tests for <c>TemporalHistoryScopeTemplate</c> — the scope behind
///     <c>IncludeHistory()</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>IncludeHistory()</c> cannot be an extension on <c>IQueryable</c>. The nested
///         <c>TemporalFilter</c> narrows a read to what is active <em>now</em>, and it is applied when
///         the queryable is built — so nothing downstream of that can widen it again. Such an
///         extension could only return its argument unchanged while promising the whole history: a
///         term with three stewards reads back as one row, with no error.
///     </para>
///     <para>
///         The lever that works is <c>IQueryFilterToggle.Disable&lt;T.TemporalFilter&gt;()</c>, which is
///         disposable and has to wrap the read. So <c>IncludeHistory()</c> is a disposable scope.
///     </para>
/// </remarks>
public class TemporalHistoryScopeTemplateTests
{
    [Fact]
    public void RenderOutput_TemporalEntity_GeneratesADisposableScope()
    {
        var source = Render(BuildTemporalEntity("Contoso.Auth", "UserRole"));

        source.Should().Contain("IncludeHistory(", "the name a reader looks for is the one that works");
        source.Should().Contain("global::System.IDisposable",
            "lifting the filter has to wrap the read, so the caller gets something to dispose");
        source.Should().Contain("IQueryFilterToggle");
    }

    /// <summary>⚠️ It lifts exactly one filter, not every filter.</summary>
    /// <remarks>
    ///     <c>FilterMode.Raw</c> and <c>QueryStrategy.Raw</c> both drop the temporal filter — and take
    ///     tenant isolation and soft-delete with them. Reading a history is not a reason to stop being
    ///     multi-tenant, which is why this disables the one filter by type.
    /// </remarks>
    [Fact]
    public void RenderOutput_DisablesOnlyTheTemporalFilter()
    {
        var source = Render(BuildTemporalEntity("Contoso.Auth", "UserRole"));

        source.Should().Contain("Disable<global::Contoso.Auth.UserRole.TemporalFilter>()",
            "the filter is named by type, so tenant and soft-delete stay on");
        source.Should().NotContain("DisableAll",
            "a history read must not become an unfiltered read");
    }

    /// <summary>It goes on the entity, so the call site names the entity and the intent.</summary>
    [Fact]
    public void RenderOutput_LandsOnTheEntityPartial()
    {
        var source = Render(BuildTemporalEntity("Contoso.Auth", "UserRole"));

        source.Should().Contain("partial class UserRole",
            "UserRole.IncludeHistory(filters) reads as the intent; an extension class name would not");
    }

    /// <summary>⚠️ The control: an entity that is not temporal gets no scope at all.</summary>
    [Fact]
    public void RenderOutput_NonTemporalEntity_GeneratesNothing()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole") with { IsTemporalRelation = false };

        new TemporalHistoryScopeTemplate(entity).RenderOutput().IsEmpty.Should().BeTrue(
            "there is no temporal filter to lift, so there is nothing to name");
    }

    private static string Render(EntityMetadataModel entity)
    {
        var artifact = new TemporalHistoryScopeTemplate(entity).RenderOutput();
        artifact.IsEmpty.Should().BeFalse();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildTemporalEntity(string ns, string name)
        => new()
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsTemporalRelation = true,
            IsValid = true,
        };
}
