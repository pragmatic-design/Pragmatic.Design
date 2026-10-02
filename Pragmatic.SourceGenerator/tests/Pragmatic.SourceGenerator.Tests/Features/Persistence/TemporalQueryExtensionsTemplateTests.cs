using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
/// Template unit tests for TemporalQueryExtensionsTemplate — pure model → output, zero Roslyn.
/// </summary>
public class TemporalQueryExtensionsTemplateTests
{
    [Fact]
    public void RenderOutput_TemporalEntity_GeneratesActiveMethod()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");
        var source = Render(entity);

        source.Should().Contain("Active(");
        // The instant is an input. UtcNow is still the fallback, and still captured app-side
        // (parameterized) to avoid app↔DB clock skew — what changed is that a caller can name it.
        source.Should().Contain("global::System.TimeProvider? timeProvider = null");
        source.Should().Contain("var now = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow;");
        source.Should().Contain("ValidFrom <= now");
        source.Should().Contain("ValidTo == null || e.ValidTo > now");
    }

    [Fact]
    public void RenderOutput_TemporalEntity_GeneratesActiveAtMethod()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");
        var source = Render(entity);

        source.Should().Contain("ActiveAt(");
        source.Should().Contain("DateTimeOffset date");
        source.Should().Contain("ValidFrom <= date");
    }

    /// <summary>⚠️ IncludeHistory is not generated in this class.</summary>
    /// <remarks>
    ///     As an extension on <c>IQueryable</c> it could only return its argument unchanged: the nested
    ///     <c>TemporalFilter</c> narrows the read before this class ever sees the query, and a method
    ///     downstream cannot widen what is already in the tree. A term with three stewards would come
    ///     back with one row, silently.
    ///     <para>
    ///         Lifting the filter has to happen before the queryable is built, so it lives on the entity
    ///         as a scope — see <c>TemporalHistoryScopeTemplateTests</c>. This asserts it is absent from
    ///         here, because having both would give a caller a working name and a no-op with the same
    ///         spelling.
    ///     </para>
    /// </remarks>
    [Fact]
    public void RenderOutput_TemporalEntity_NoLongerGeneratesTheNoOpIncludeHistory()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");
        var source = Render(entity);

        source.Should().NotContain("IncludeHistory(",
            "a query extension cannot lift a filter that is already in the tree");

        // The control: the extensions that CAN work downstream are still generated here.
        source.Should().Contain("ActiveAt(");
        source.Should().Contain("Active(");
    }

    [Fact]
    public void RenderOutput_ClassName_UsesNamingHelper()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");
        var source = Render(entity);

        source.Should().Contain("class UserRoleTemporalExtensions");
    }

    [Fact]
    public void RenderOutput_HintName_UsesVirtualFolder()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");

        var template = new TemporalQueryExtensionsTemplate(entity);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("UserRole");
        artifact.HintName.Should().Contain("TemporalExtensions");
    }

    [Fact]
    public void RenderOutput_NonTemporalEntity_ReturnsEmpty()
    {
        var entity = new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Contoso.Sales.Invoice",
            Namespace = "Contoso.Sales",
            IdType = "Guid",
            IsTemporalRelation = false,
            IsValid = true
        };

        var template = new TemporalQueryExtensionsTemplate(entity);
        var artifact = template.RenderOutput();

        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void RenderOutput_MethodsAreStatic()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");
        var source = Render(entity);

        source.Should().Contain("static class UserRoleTemporalExtensions");
    }

    [Fact]
    public void RenderOutput_UsesFullyQualifiedEntityType()
    {
        var entity = BuildTemporalEntity("Contoso.Auth", "UserRole");
        var source = Render(entity);

        source.Should().Contain("global::Contoso.Auth.UserRole");
    }

    private static string Render(EntityMetadataModel entity)
    {
        var template = new TemporalQueryExtensionsTemplate(entity);
        var artifact = template.RenderOutput();
        artifact.IsEmpty.Should().BeFalse();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildTemporalEntity(
        string ns, string name, int maxActive = 0, bool allowOverlap = false)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsTemporalRelation = true,
            TemporalMaxActive = maxActive,
            TemporalAllowOverlap = allowOverlap,
            IsValid = true
        };
    }
}
