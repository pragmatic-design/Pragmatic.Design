namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>xUnit collection sharing one PostgreSQL container across the SQL transport tests.</summary>
[CollectionDefinition("SqlTransportPostgres")]
public sealed class SqlTransportPostgresCollection : ICollectionFixture<SqlTransportPostgresFixture>;
