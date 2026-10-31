using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Cryptography;
using Pragmatic.Persistence.Sql;
using Pragmatic.Privacy;
using Pragmatic.Privacy.Engines.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The subject registry and the consent store on the Sql engine (Pragmatic.Privacy.Engines), on five databases: their tables as the generated
///     schema has them (EF Core's <c>__Subjects</c> and <c>__Consents</c>) in the database that holds a <c>[DataSubject]</c>, migrated with the
///     others; references allocated, found and resolved; subjects forgotten, as many as there are, the blind index unique among the others.
/// </summary>
[Collection("pragmatic databases")]
public class SubjectRegistryTests(PragmaticDatabases databases)
{
    [Fact]
    public void The_registrys_tables_in_the_schema_are_EF_Cores_column_for_column()
    {
        // SchemaMetadataTransform's registry tables on SQL Server, the EF Core form: a database where both write the registry holds one.
        var tables = DialectSchemas.For(SqlDialect.SqlServer).Tables;
        var subjects = tables.Single(t => t.Name == "__Subjects");
        Assert.Equal([("SubjectRef", "nvarchar(64)", false, true), ("SubjectType", "nvarchar(128)", false, false), ("LookupIndex", "varbinary(32)", true, false),
                ("Identifier", "varbinary(max)", true, false), ("CreatedAt", "bigint", false, false), ("ForgottenAt", "bigint", true, false)],
            subjects.Columns.Select(c => (c.Name, c.SqlType, c.IsNullable, c.IsPrimaryKey)));
        var lookup = Assert.Single(subjects.Indexes);
        Assert.Equal(("IX___Subjects_LookupIndex", true, "\"LookupIndex\" IS NOT NULL"), (lookup.Name, lookup.IsUnique, lookup.Filter));

        var consents = tables.Single(t => t.Name == "__Consents");
        Assert.Equal(["SubjectRef", "Purpose", "NoticeVersion"], consents.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name));
        Assert.Equal(["SubjectRef", "Purpose", "NoticeVersion", "GrantedAt", "WithdrawnAt", "Source"], consents.Columns.Select(c => c.Name));
        Assert.Equal(["IX___Consents_SubjectRef"], consents.Indexes.Select(i => i.Name));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task References_are_found_resolved_and_forgotten_and_any_number_of_subjects_can_be_forgotten(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        await using var provider = Registry(session);
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISubjectRegistry>();
        var ada = $"ada-{Guid.NewGuid():N}@example.com";
        var bob = $"bob-{Guid.NewGuid():N}@example.com";

        var reference = await registry.GetOrCreateReferenceAsync("Guest", ada);
        Assert.Equal(reference, await registry.GetOrCreateReferenceAsync("Guest", ada));
        Assert.NotEqual(reference, await registry.GetOrCreateReferenceAsync("Employee", ada)); // two relationships, two subjects
        Assert.Equal(reference, await registry.FindReferenceAsync("Guest", ada));
        Assert.Equal(ada, await registry.ResolveIdentityAsync(reference));
        var row = await session.Query<SubjectRow>().Where((s, r) => s.SubjectRef == r, reference).FirstOrDefaultAsync();
        Assert.DoesNotContain(ada, System.Text.Encoding.UTF8.GetString(row!.Identifier!)); // stored encrypted
        Assert.Equal(SubjectLookup.BlindIndexLength, row.LookupIndex!.Length);

        // Two subjects forgotten: their blind indexes are both null, and the unique index holds them (SQL Server only with its filter).
        var other = await registry.GetOrCreateReferenceAsync("Guest", bob);
        Assert.True(await registry.ForgetAsync(reference));
        Assert.True(await registry.ForgetAsync(other));
        Assert.False(await registry.ForgetAsync(reference));
        Assert.Null(await registry.ResolveIdentityAsync(reference));
        Assert.Null(await registry.FindReferenceAsync("Guest", ada));

        // An erased identity coming back is a new subject, and the old reference stays erased.
        var returned = await registry.GetOrCreateReferenceAsync("Guest", ada);
        Assert.NotEqual(reference, returned);
        Assert.Equal(ada, await registry.ResolveIdentityAsync(returned));
        Assert.Null(await registry.ResolveIdentityAsync(reference));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_consent_is_per_notice_version_withdrawn_for_every_version_and_kept_in_the_history(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        await using var provider = Registry(session);
        await using var scope = provider.CreateAsyncScope();
        var consents = scope.ServiceProvider.GetRequiredService<IConsentStore>();
        var subject = SubjectLookup.NewReference();

        await consents.GrantAsync(subject, "newsletter", "v1", "signup form");
        await consents.GrantAsync(subject, "newsletter", "v2");
        Assert.True(await consents.IsGrantedAsync(subject, "newsletter", "v1"));
        Assert.False(await consents.IsGrantedAsync(subject, "newsletter", "v3")); // an older notice does not cover a newer one

        Assert.True(await consents.WithdrawAsync(subject, "newsletter", UnitOfWorkTests.Now));
        Assert.False(await consents.IsGrantedAsync(subject, "newsletter", "v1"));
        Assert.False(await consents.IsGrantedAsync(subject, "newsletter", "v2"));
        Assert.False(await consents.WithdrawAsync(subject, "newsletter", UnitOfWorkTests.Now));

        await consents.GrantAsync(subject, "newsletter", "v1"); // the same fact reactivated
        var history = await consents.GetHistoryAsync(subject);
        Assert.Equal([("v1", true, "signup form"), ("v2", false, null)], history.OrderBy(c => c.NoticeVersion).Select(c => (c.NoticeVersion, c.IsActive, c.Source)));
    }

    /// <summary>The registry of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration adds it.</summary>
    private static ServiceProvider Registry(SqlSession session)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(SubjectRegistryTests), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ISecretEncryptor>(new AesGcmSecretEncryptor(new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32)))));
        services.AddSingleton<ISubjectLookupKeyProvider>(new FixedLookupKey());
        services.AddSqlSubjectRegistry<SubjectRegistryTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class FixedLookupKey : ISubjectLookupKeyProvider
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(_key);
    }
}
