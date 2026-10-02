using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the RepositoryTemplate which generates repository classes with CRUD operations.
/// </summary>
public class RepositoryTemplateTests
{
    [Fact]
    public void SimpleEntity_GeneratesRepositoryClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("class Repository");
    }

    [Fact]
    public void Repository_GeneratesDbSetProperty()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("DbSet<");
        source.Should().Contain("Order");
    }

    [Fact]
    public void Repository_GeneratesGetByIdAsync()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("GetByIdAsync");
        source.Should().Contain("PersistenceId");
    }

    [Fact]
    public void Repository_GeneratesAddMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Add(");
    }

    [Fact]
    public void Repository_GeneratesAddRangeMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("AddRange(");
    }

    [Fact]
    public void Repository_GeneratesRemoveMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Remove(");
    }

    [Fact]
    public void Repository_GeneratesSaveChangesAsync()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("SaveChangesAsync");
    }

    [Fact]
    public void Entity_WithLogicKey_GeneratesGetByLogicKey()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = [new LogicKeyPart { Name = "OrderNumber", TypeName = "string" }]
        };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("GetByOrderNumberAsync");
        source.Should().Contain("OrderNumber");
    }

    [Fact]
    public void Entity_WithoutLogicKey_NoGetByLogicKey()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = []
        };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("GetByLogicKey");
    }

    [Fact]
    public void Entity_WithSoftDelete_GeneratesSoftDeleteRemove()
    {
        // Arrange
        var model = CreateBasicModel() with { IsSoftDelete = true };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IsDeleted");
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Entities;");
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("Order");
        artifact.HintName.Should().Contain("Repository");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        // Arrange
        var model = CreateBasicModel() with { IsValid = false };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Repository_GeneratesConstructor()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Repository(");
    }

    [Fact]
    public void RealisticScenario_CourseEntity()
    {
        // Arrange - Contoso University Course entity
        var model = new EntityMetadataModel
        {
            TypeName = "Course",
            FullTypeName = "Contoso.University.Catalog.Entities.Course",
            Namespace = "Contoso.University.Catalog.Entities",
            IdType = "System.Guid",
            LogicKeys = [new LogicKeyPart { Name = "CourseCode", TypeName = "string" }],
            BoundaryName = "Catalog",
            IsSoftDelete = true,
            IsAuditable = true,
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "CourseCode",
                    TypeName = "string",
                    IsLogicKey = true,
                    IsRequiredForCreate = true
                },
                new PropertyMetadataModel
                {
                    Name = "Title",
                    TypeName = "string",
                    IsRequiredForCreate = true
                },
                new PropertyMetadataModel
                {
                    Name = "Credits",
                    TypeName = "int",
                    IsRequiredForCreate = true
                }
            )
        };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.University.Catalog.Entities;");
        source.Should().Contain("class Repository");
        source.Should().Contain("GetByIdAsync");
        source.Should().Contain("GetByCourseCodeAsync");
        source.Should().Contain("SaveChangesAsync");
        source.Should().Contain("IsDeleted");
    }

    [Fact]
    public void Entity_WithConcurrencyAware_GeneratesSaveChangesWithConcurrencyHandling()
    {
        // Arrange
        var model = CreateBasicModel() with { IsConcurrencyAware = true };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("DbUpdateConcurrencyException");
        source.Should().Contain("ConcurrencyError");
        source.Should().Contain("Result<int");
    }

    [Fact]
    public void Entity_WithConcurrencyAware_ReturnsResultType()
    {
        // Arrange
        var model = CreateBasicModel() with { IsConcurrencyAware = true };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // The return type should use the Result pattern
        source.Should().Contain("Result<int, global::Pragmatic.Persistence.Repository.ConcurrencyError>");
    }

    [Fact]
    public void Entity_WithoutConcurrencyAware_GeneratesSimpleSaveChanges()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("DbUpdateConcurrencyException");
        source.Should().NotContain("ConcurrencyError");
        source.Should().Contain("return _unitOfWork.SaveChangesAsync(ct);",
            "the unit of work is the one place that classifies what the database refused, hands over "
            + "the entity's domain events and records the save — saving through the context skipped all three");
    }

    [Fact]
    public void Entity_WithConcurrencyAware_IncludesEntityTypeNameInError()
    {
        // Arrange
        var model = CreateBasicModel() with { IsConcurrencyAware = true };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("EntityTypeName = \"MyApp.Sales.Entities.Order\"");
    }

    [Fact]
    public void Repository_GeneratesBulkUpdateAsync()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("BulkUpdateAsync(");
        source.Should().Contain("Specification<");
        source.Should().Contain("UpdateSettersBuilder<");
        source.Should().Contain("ExecuteUpdateAsync(updateAction, ct)");
    }

    [Fact]
    public void Repository_GeneratesBulkDeleteAsync_ForRegularEntity()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("BulkDeleteAsync(");
        source.Should().Contain("ExecuteDeleteAsync(ct)");
        source.Should().NotContain("Soft delete - mark as deleted");
    }

    [Fact]
    public void Entity_WithSoftDelete_BulkDeleteAsync_UsesSoftDeleteUpdate()
    {
        // Arrange
        var model = CreateBasicModel() with { IsSoftDelete = true };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // BulkDeleteAsync should use ExecuteUpdateAsync (not ExecuteDeleteAsync) for soft-delete entities
        source.Should().Contain("BulkDeleteAsync(");
        source.Should().Contain("Soft delete - mark as deleted");
        source.Should().Contain("SetProperty(e => e.IsDeleted, true)");
        // Bulk soft-delete now uses the injected TimeProvider (testable time), not DateTimeOffset.UtcNow.
        source.Should().Contain("var now = _timeProvider.GetUtcNow();");
        source.Should().Contain("SetProperty(e => e.DeletedAt, now)");

        // Should NOT contain physical delete
        source.Should().NotContain("ExecuteDeleteAsync");
    }

    [Fact]
    public void Entity_WithoutSoftDelete_BulkDeleteAsync_UsesPhysicalDelete()
    {
        // Arrange
        var model = CreateBasicModel() with { IsSoftDelete = false };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("BulkDeleteAsync(");
        source.Should().Contain("ExecuteDeleteAsync(ct)");
    }

    [Fact]
    public void BulkUpdateAsync_UsesSpecificationFilter()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Verifies the specification's ToExpression() is used in the Where clause
        source.Should().Contain("filter.ToExpression()");
    }

    [Fact]
    public void BulkDeleteAsync_UsesSpecificationFilter()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Bulk delete must route through ApplyRootFilters(Set) so ownership/scoped record-level
        // security is enforced (the raw Set bypasses it), then apply the caller specification.
        source.Should().Contain("ApplyRootFilters(Set).Where(filter.ToExpression()).ExecuteDeleteAsync");
        source.Should().NotContain("Set.Where(filter.ToExpression()).ExecuteDeleteAsync");
    }

    [Fact]
    public void BulkOperations_RouteThroughApplyRootFilters_ForRecordLevelSecurity()
    {
        // Without this, [HasOwner]/[HasAccessScopes] bulk update/delete would build Set.Where(spec)
        // on the raw DbSet, letting a caller mutate/delete rows owned by other users in the same tenant.
        var model = CreateBasicModel();

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        // The root-only filter helper is generated and used by both bulk paths.
        source.Should().Contain("IQueryable<").And.Contain("ApplyRootFilters(");
        source.Should().Contain("ApplyRootFilters(Set).Where(filter.ToExpression()).ExecuteUpdateAsync");
    }

    [Fact]
    public void RealisticScenario_SoftDeleteEntity_GeneratesBothBulkMethods()
    {
        // Arrange - Course entity with soft-delete
        var model = new EntityMetadataModel
        {
            TypeName = "Course",
            FullTypeName = "Contoso.University.Catalog.Entities.Course",
            Namespace = "Contoso.University.Catalog.Entities",
            IdType = "System.Guid",
            LogicKeys = [new LogicKeyPart { Name = "CourseCode", TypeName = "string" }],
            BoundaryName = "Catalog",
            IsSoftDelete = true,
            IsAuditable = true,
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "CourseCode",
                    TypeName = "string",
                    IsLogicKey = true,
                    IsRequiredForCreate = true
                }
            )
        };

        // Act
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Both bulk methods should be present
        source.Should().Contain("BulkUpdateAsync(");
        source.Should().Contain("BulkDeleteAsync(");

        // BulkDeleteAsync should use soft-delete pattern
        source.Should().Contain("SetProperty(e => e.IsDeleted, true)");
        source.Should().Contain("SetProperty(e => e.DeletedAt");
        source.Should().NotContain("ExecuteDeleteAsync");
    }

    #region BulkDescriptor

    [Fact]
    public void Repository_GeneratesBulkDescriptor()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkEntityDescriptor<");
        source.Should().Contain("_bulkDescriptor");
        source.Should().Contain("Columns =");
        source.Should().Contain("ReadValue =");
    }

    [Fact]
    public void BulkDescriptor_AssignsKeyRoleToPersistenceId()
    {
        var model = CreateModelWithAllProperties();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("(\"PersistenceId\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.Key)");
    }

    [Fact]
    public void BulkDescriptor_AssignsLogicKeyRole()
    {
        var model = CreateModelWithAllProperties();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("(\"Sku\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.LogicKey)");
    }

    [Fact]
    public void BulkDescriptor_AuditableEntity_AssignsInsertOnlyAndUpdateOnlyRoles()
    {
        var model = CreateModelWithAllProperties();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("(\"CreatedAt\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.InsertOnly)");
        source.Should().Contain("(\"CreatedBy\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.InsertOnly)");
        source.Should().Contain("(\"UpdatedAt\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.UpdateOnly)");
        source.Should().Contain("(\"UpdatedBy\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.UpdateOnly)");
    }

    [Fact]
    public void BulkDescriptor_SoftDeleteEntity_AssignsSoftDeleteRoles()
    {
        var model = CreateModelWithAllProperties();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("(\"IsDeleted\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.SoftDelete)");
        source.Should().Contain("(\"DeletedAt\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.SoftDelete)");
        source.Should().Contain("(\"DeletedBy\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.SoftDelete)");
    }

    [Fact]
    public void BulkDescriptor_ConcurrencyAwareEntity_AssignsComputedRoleToRowVersion()
    {
        var model = CreateModelWithAllProperties() with { IsConcurrencyAware = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("(\"RowVersion\", global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.Computed)");
    }

    [Fact]
    public void BulkDescriptor_EnumProperty_CastsToInt()
    {
        var model = CreateModelWithAllProperties();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("\"Status\" => (int)entity.Status");
    }

    [Fact]
    public void BulkDescriptor_NullableProperty_CastsToObjectNullable()
    {
        var model = CreateModelWithAllProperties();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("\"Description\" => (object?)entity.Description");
    }

    #endregion

    #region BulkInsertAsync

    [Fact]
    public void Repository_GeneratesBulkInsertAsync()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkInsertAsync(");
        source.Should().Contain("BulkExecutor.InsertAsync(");
        source.Should().Contain("BulkInsertOptions");
    }

    [Fact]
    public void BulkInsertAsync_BasicEntity_PassesNullAuditFields()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkExecutor.InsertAsync(_db, entities, _bulkDescriptor, null, null, options, ct)");
    }

    [Fact]
    public void BulkInsertAsync_AuditableEntity_PassesTimestampAndUserId()
    {
        var model = CreateBasicModel() with { IsAuditable = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("_timeProvider.GetUtcNow()");
        source.Should().Contain("_currentUser?.IdOrNull()");
        source.Should().Contain("BulkExecutor.InsertAsync(_db, entities, _bulkDescriptor, now, userId, options, ct)");
    }

    [Fact]
    public void BulkInsertAsync_GeneratesConvenienceOverload()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("=> BulkInsertAsync(entities, null, ct)");
    }

    #endregion

    #region BulkUpsertAsync

    [Fact]
    public void Repository_GeneratesBulkUpsertAsync()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkUpsertAsync(");
        source.Should().Contain("BulkExecutor.UpsertAsync(");
        source.Should().Contain("UpsertOptions");
    }

    [Fact]
    public void BulkUpsertAsync_BasicEntity_PassesNullAuditFields()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkExecutor.UpsertAsync(_db, entities, _bulkDescriptor, null, null, options, ct)");
    }

    [Fact]
    public void BulkUpsertAsync_SoftDeleteEntity_PassesTimestampAndUserId()
    {
        var model = CreateBasicModel() with { IsSoftDelete = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkExecutor.UpsertAsync(_db, entities, _bulkDescriptor, now, userId, options, ct)");
    }

    [Fact]
    public void BulkUpsertAsync_GeneratesConvenienceOverload()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("=> BulkUpsertAsync(entities, null, ct)");
    }

    #endregion

    #region UpsertAsync (single)

    [Fact]
    public void Repository_GeneratesUpsertAsync()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("UpsertAsync(");
        source.Should().Contain("BulkExecutor.UpsertSingleAsync(");
        source.Should().Contain("UpsertMatch matchOn");
    }

    [Fact]
    public void UpsertAsync_BasicEntity_PassesNullAuditFields()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkExecutor.UpsertSingleAsync(_db, entity, _bulkDescriptor, null, null, matchOn, ct)");
    }

    [Fact]
    public void UpsertAsync_AuditableEntity_PassesTimestampAndUserId()
    {
        var model = CreateBasicModel() with { IsAuditable = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BulkExecutor.UpsertSingleAsync(_db, entity, _bulkDescriptor, now, userId, matchOn, ct)");
    }

    [Fact]
    public void UpsertAsync_DefaultMatchOnIsPrimaryKey()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("UpsertMatch matchOn = global::Pragmatic.Persistence.EFCore.Bulk.UpsertMatch.PrimaryKey");
    }

    [Fact]
    public void UpsertAsync_GeneratesConvenienceOverload()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("=> UpsertAsync(entity, global::Pragmatic.Persistence.EFCore.Bulk.UpsertMatch.PrimaryKey, ct)");
    }

    #endregion

    #region Constructor — auditable entities

    [Fact]
    public void AuditableEntity_GeneratesTimeProviderAndCurrentUserFields()
    {
        var model = CreateBasicModel() with { IsAuditable = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("TimeProvider _timeProvider");
        source.Should().Contain("ICurrentUser? _currentUser");
    }

    [Fact]
    public void AuditableEntity_GeneratesConstructorWithTimeProviderAndCurrentUser()
    {
        var model = CreateBasicModel() with { IsAuditable = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("TimeProvider? timeProvider = null");
        source.Should().Contain("ICurrentUser? currentUser = null");
        source.Should().Contain("_timeProvider = timeProvider ?? global::System.TimeProvider.System");
        source.Should().Contain("_currentUser = currentUser");
    }

    [Fact]
    public void BasicEntity_HasAClockAndNoCurrentUser()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // The clock is unconditional: every repository builds a FilterContext, and FilterContext.Now
        // is required precisely so the instant a read evaluates against can be pinned. This test used
        // to assert the opposite, and its name said so — a basic entity got no _timeProvider and the
        // generated code filled that required property from DateTimeOffset.UtcNow instead.
        source.Should().Contain("_timeProvider");

        // The user is not: nothing on a plain entity records who did anything.
        source.Should().NotContain("_currentUser");
    }

    #endregion

    [Fact]
    public void AuditedEntity_BulkDelete_WritesSyntheticAuditRow()
    {
        // [Audited] entity: ExecuteDelete bypasses the change tracker, so the repository must write
        // one aggregate audit entry itself, or the audit trail silently diverges.
        var model = CreateBasicModel() with { IsAudited = true };

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        source.Should().Contain("async global::System.Threading.Tasks.Task<int> BulkDeleteAsync");
        source.Should().Contain("global::Pragmatic.Audit.EFCore.AuditEntryStaging.Stage");
        source.Should().Contain("Operation = \"Data.EntityBulkDeleted\"");
        source.Should().Contain("if (__affected > 0)");
    }

    [Fact]
    public void AuditedEntity_BulkUpdate_WritesSyntheticAuditRow()
    {
        var model = CreateBasicModel() with { IsAudited = true };

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        source.Should().Contain("async global::System.Threading.Tasks.Task<int> BulkUpdateAsync");
        source.Should().Contain("Operation = \"Data.EntityBulkUpdated\"");
    }

    [Fact]
    public void AuditedSoftDeleteEntity_BulkDelete_SoftDeletesAndAudits()
    {
        var model = CreateBasicModel() with { IsAudited = true, IsSoftDelete = true };

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        // Soft-delete via ExecuteUpdate, then the synthetic audit row.
        source.Should().Contain("s.SetProperty(e => e.IsDeleted, true)");
        source.Should().Contain("Operation = \"Data.EntityBulkDeleted\"");
    }

    [Fact]
    public void NonAuditedEntity_BulkDelete_NoSyntheticAuditRow()
    {
        // Without [Audited] the fast ExecuteDelete path is used and no audit row is written.
        var model = CreateBasicModel();

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        source.Should().NotContain("AuditLogEntry");
        source.Should().Contain("ExecuteDeleteAsync");
    }

    [Fact]
    public void Repository_BulkDelete_RoutesRollUpChildrenThroughTrackedPath()
    {
        // Roll-up participation is resolved from DI at runtime, so the guard + tracked delete path is
        // always emitted (ExecuteDelete bypasses the RollUpInterceptor, drifting parent aggregates).
        var model = CreateBasicModel();

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        source.Should().Contain("_participatesInRollUp");
        source.Should().Contain("if (_participatesInRollUp)");
        source.Should().Contain("ToListAsync");
        source.Should().Contain("_db.RemoveRange(__tracked)");
        source.Should().Contain("global::Pragmatic.Persistence.RollUp.RollUpRule"); // ctor param
    }

    [Fact]
    public void SoftDeleteEntity_BulkDelete_RollUpTrackedPath_SoftDeletesInMemory()
    {
        var model = CreateBasicModel() with { IsSoftDelete = true };

        var source = new RepositoryTemplate(model).RenderOutput().Text;

        // The tracked path flips the soft-delete flags in memory so the interceptor observes the change.
        source.Should().Contain("__e.IsDeleted = true;");
    }

    private static EntityMetadataModel CreateBasicModel()
    {
        return new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            BoundaryName = "Sales",
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "OrderNumber",
                    TypeName = "string",
                    IsRequiredForCreate = true
                }
            )
        };
    }

    /// <summary>
    ///     Creates a model with all property types for comprehensive BulkDescriptor testing.
    /// </summary>
    private static EntityMetadataModel CreateModelWithAllProperties()
    {
        return new EntityMetadataModel
        {
            TypeName = "Product",
            FullTypeName = "MyApp.Catalog.Entities.Product",
            Namespace = "MyApp.Catalog.Entities",
            IdType = "System.Guid",
            BoundaryName = "Catalog",
            LogicKeys = [new LogicKeyPart { Name = "Sku", TypeName = "string" }],
            IsAuditable = true,
            IsSoftDelete = true,
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel { Name = "PersistenceId", TypeName = "System.Guid" },
                new PropertyMetadataModel { Name = "Sku", TypeName = "string", IsLogicKey = true, IsRequiredForCreate = true },
                new PropertyMetadataModel { Name = "Name", TypeName = "string", IsRequiredForCreate = true },
                new PropertyMetadataModel { Name = "Description", TypeName = "string", IsNullable = true },
                new PropertyMetadataModel { Name = "Price", TypeName = "decimal", IsRequiredForCreate = true },
                new PropertyMetadataModel { Name = "Status", TypeName = "ProductStatus", IsEnum = true },
                new PropertyMetadataModel { Name = "CreatedAt", TypeName = "System.DateTimeOffset" },
                new PropertyMetadataModel { Name = "CreatedBy", TypeName = "string", IsNullable = true },
                new PropertyMetadataModel { Name = "UpdatedAt", TypeName = "System.DateTimeOffset", IsNullable = true },
                new PropertyMetadataModel { Name = "UpdatedBy", TypeName = "string", IsNullable = true },
                new PropertyMetadataModel { Name = "IsDeleted", TypeName = "bool" },
                new PropertyMetadataModel { Name = "DeletedAt", TypeName = "System.DateTimeOffset", IsNullable = true },
                new PropertyMetadataModel { Name = "DeletedBy", TypeName = "string", IsNullable = true },
                new PropertyMetadataModel { Name = "RowVersion", TypeName = "byte[]" }
            )
        };
    }

    // --- DataSource Strategy Tests ---

    [Fact]
    public void Repository_GeneratesQueryWithQueryStrategy()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("Query(global::Pragmatic.Persistence.Query.QueryStrategy strategy)");
        source.Should().Contain("QueryStrategy.Projection => ApplyFilters(Set.AsNoTracking())");
        source.Should().Contain("QueryStrategy.Entity => ApplyFilters(Set)");
        source.Should().Contain("QueryStrategy.Filtered => ApplyFilters(Set)");
        source.Should().Contain("QueryStrategy.Raw => Set.AsNoTracking().AsQueryable()");
    }

    [Fact]
    public void Repository_DefaultQueryDelegatesToFiltered()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("Query(global::Pragmatic.Persistence.Query.QueryStrategy.Filtered)");
    }

    // --- FilterMapComposer Integration Tests ---

    [Fact]
    public void Repository_GeneratesFilterMapComposerField()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("FilterMapComposer? _filterMapComposer");
    }

    [Fact]
    public void Repository_GeneratesTenantContextField()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("ITenantContext? _tenantContext");
    }

    [Fact]
    public void Repository_GeneratesFilterToggleField()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("IQueryFilterToggle? _filterToggle");
    }

    [Fact]
    public void Repository_ConstructorAcceptsFilterMapComposer()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("FilterMapComposer? filterMapComposer = null");
    }

    [Fact]
    public void Repository_ApplyFilters_IncludesNavigationFiltering()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("_filterMapComposer.ApplyNavigationFilters");
        source.Should().Contain("BuildFilterContext()");
    }

    [Fact]
    public void Repository_GeneratesBuildFilterContext()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("BuildFilterContext()");
        source.Should().Contain("_tenantContext?.TenantId");
        source.Should().Contain("_filterToggle?.CurrentMode");
    }

    [Fact]
    public void AuditableEntity_BuildFilterContext_UsesCurrentUser()
    {
        var model = CreateBasicModel() with { IsAuditable = true };
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("_currentUser?.IdOrNull()");
        source.Should().Contain("_timeProvider.GetUtcNow()");
    }

    [Fact]
    public void SimpleEntity_BuildFilterContext_UsesDefaults()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // The instant comes from the injected clock, whatever traits the entity carries: otherwise
        // whether an application could pin its own reads would depend on the entity.
        source.Should().Contain("Now = _timeProvider.GetUtcNow(),");
        source.Should().NotContain("Now = global::System.DateTimeOffset.UtcNow,");

        // No _currentUser for non-auditable
        source.Should().NotContain("_currentUser?.IdOrNull()");
    }

    [Fact]
    public void Repository_ApplyFilters_RootAndNavigationBothPresent()
    {
        var model = CreateBasicModel();
        var template = new RepositoryTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Root-level filtering still present
        source.Should().Contain("_filterProvider");
        source.Should().Contain("GetCombinedFilter");

        // Navigation-level filtering added
        source.Should().Contain("_filterMapComposer");
        source.Should().Contain("ApplyNavigationFilters");
    }
}
