using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic;
using Pragmatic.Composition;
using Pragmatic.Migrations;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;
using Pragmatic.Migrations.Core.Tests;
using Pragmatic.Migrations.Core.Tests.Unit;
using Pragmatic.Migrations.Core.Tests.Unit.Configuration;
using Pragmatic.Migrations.Core.Tests.Unit.Runner;
using Pragmatic.Migrations.Core.Tests.Unit.Sql;
using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IConnectionFactory>]
[assembly: GenerateMock<IPragmaticBuilder>]
[assembly: GenerateMock<ISchemaDiffEngine>]
[assembly: GenerateMock<ISchemaIntrospector>]
[assembly: GenerateMock<ISqlMigrationGenerator>]
