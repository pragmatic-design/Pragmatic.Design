using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Serialization;

namespace Pragmatic.Persistence.Tests.Serialization;

/// <summary>
///     Verifies <see cref="EntityJsonModifier.ExcludeInfrastructureProperties"/> composes with the shape
///     the W3 generated <c>PragmaticJsonContext</c> produces: a <see cref="JsonTypeInfo"/> built via
///     <c>CreateJsonTypeInfo</c> + <c>CreateJsonPropertyInfo</c> whose JSON names are already camelCased
///     (the generator applies the naming policy at emit time). The modifier must strip infrastructure
///     properties regardless of the casing STJ reports for the property name.
/// </summary>
public sealed class EntityJsonModifierTests
{
    /// <remarks>
    ///     It implements <see cref="Pragmatic.Persistence.Entity.IChangeTracking" /> because the subject
    ///     here is the casing, and the change-tracking names are stripped on the types that
    ///     declare them rather than on every type by name.
    /// </remarks>
    private sealed class SampleEntity : Pragmatic.Persistence.Entity.IChangeTracking
    {
        public string Name { get; set; } = "";
        public Guid TenantId { get; set; }
        public bool IsNew { get; set; }

        public IReadOnlySet<string> ModifiedProperties => new HashSet<string>();
        public IReadOnlySet<string> CollectionsModified => new HashSet<string>();

        public void ResetModifiedProperties() { }
    }

    /// <summary>Mirrors the generated context: manual JsonTypeInfo with camelCase JSON property names.</summary>
    private static JsonTypeInfo<SampleEntity> BuildGeneratedStyleTypeInfo(JsonSerializerOptions options)
    {
        var info = JsonTypeInfo.CreateJsonTypeInfo<SampleEntity>(options);
        info.CreateObject = static () => new SampleEntity();

        var name = info.CreateJsonPropertyInfo(typeof(string), "name");
        name.Get = static o => ((SampleEntity)o).Name;
        name.Set = static (o, v) => ((SampleEntity)o).Name = (string)v!;
        info.Properties.Add(name);

        var tenant = info.CreateJsonPropertyInfo(typeof(Guid), "tenantId");
        tenant.Get = static o => ((SampleEntity)o).TenantId;
        tenant.Set = static (o, v) => ((SampleEntity)o).TenantId = (Guid)v!;
        info.Properties.Add(tenant);

        var isNew = info.CreateJsonPropertyInfo(typeof(bool), "isNew");
        isNew.Get = static o => ((SampleEntity)o).IsNew;
        isNew.Set = static (o, v) => ((SampleEntity)o).IsNew = (bool)v!;
        info.Properties.Add(isNew);

        return info;
    }

    [Fact]
    public void ExcludeInfrastructureProperties_OnGeneratedStyleTypeInfo_StripsCamelCaseInfraProps()
    {
        var options = new JsonSerializerOptions
        {
            // GeneratedStyleResolver owns SampleEntity (mirrors the W3 context); the default resolver
            // supplies the leaf types (string/Guid/bool) our context would also cover.
            TypeInfoResolver = JsonTypeInfoResolver
                .Combine(new GeneratedStyleResolver(), new DefaultJsonTypeInfoResolver())
                .WithAddedModifier(EntityJsonModifier.ExcludeInfrastructureProperties)
        };

        var json = JsonSerializer.Serialize(new SampleEntity { Name = "widget", TenantId = Guid.NewGuid(), IsNew = true }, options);

        json.Should().Contain("\"name\"");
        json.Should().NotContain("tenantId", "the modifier must strip TenantId even when its JSON name is camelCased");
        json.Should().NotContain("isNew", "the modifier must strip IsNew even when its JSON name is camelCased");
    }

    private sealed class GeneratedStyleResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            => type == typeof(SampleEntity) ? BuildGeneratedStyleTypeInfo(options) : null;
    }

    private sealed class AuthzEntity
    {
        public string Name { get; set; } = "";
        public Guid OwnerId { get; set; }
        public List<string> AccessScopes { get; set; } = [];
        public uint RowVersion { get; set; }
        public long PersistenceId { get; set; }
    }

    [Fact]
    public void ExcludeInfrastructureProperties_StripsOwnershipAndScopeInternals()
    {
        // OwnerId/AccessScopes ([HasOwner]/[HasAccessScopes]) and RowVersion/PersistenceId are
        // authorization/infrastructure internals. Leaking them in an API response reveals who owns the
        // record and which scopes make it visible ("user:{id}") — reconnaissance for privilege
        // escalation. They are stripped alongside TenantId.
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
                .WithAddedModifier(EntityJsonModifier.ExcludeInfrastructureProperties)
        };

        var json = JsonSerializer.Serialize(
            new AuthzEntity { Name = "widget", OwnerId = Guid.NewGuid(), AccessScopes = { "user:1" }, RowVersion = 3, PersistenceId = 42 },
            options);

        json.Should().Contain("Name");
        json.Should().NotContain("OwnerId", "ownership must not leak to clients");
        json.Should().NotContain("AccessScopes", "the visibility scopes must not leak to clients");
        json.Should().NotContain("RowVersion");
        json.Should().NotContain("PersistenceId");
    }
}
