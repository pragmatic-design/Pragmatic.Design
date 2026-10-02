namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>xUnit collection sharing one SQL Server container across the SQL transport tests.</summary>
[CollectionDefinition("SqlTransportMsSql")]
public sealed class SqlTransportMsSqlCollection : ICollectionFixture<SqlTransportMsSqlFixture>;
