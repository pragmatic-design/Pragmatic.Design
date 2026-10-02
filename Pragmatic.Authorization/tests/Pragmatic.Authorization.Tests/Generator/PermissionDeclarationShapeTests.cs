using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     Which permission and <c>IRole</c> declarations the generator actually finds.
///     <para>
///         A declaration the generator does not find, or a name it cannot read, would otherwise be
///         dropped without a word: no constant, no registry entry, no catalog entry — and therefore a
///         permission nobody can ever be granted. So a value taken from a constant
///         (<c>PermissionNames.Read</c>) must resolve like a literal written in place, and one that
///         cannot be resolved is reported. A permission is an <c>[assembly: Permission]</c> line; a role
///         is a type.
///     </para>
///     <para>
///         The assertions read the generated registry, because that is what the runtime consumes: a
///         model-level check would pass for a permission that never reaches the catalog.
///     </para>
/// </summary>
public class PermissionDeclarationShapeTests : AuthorizationGeneratorTestBase
{
    private static string? Registry(SourceGenRunResult result)
        => GetGeneratedSource(result, "PermissionRegistry");

    [Fact]
    public void ADeclaredPermission_IsRegistered_WithItsDescription()
    {
        var result = RunGenerator("""
            using Pragmatic.Authorization;

            [assembly: Permission("billing.invoices.read", "Read the invoices", Category = "Billing")]

            namespace TestApp.Billing;
            """);

        Registry(result).Should().NotBeNull()
            .And.Contain("new PermissionInfo(\"billing.invoices.read\", \"Read the invoices\", \"Billing\")");
    }

    [Fact]
    public void APermissionValueFromAConstant_IsResolved()
    {
        // A const reference is every bit as determinate as a literal.
        var result = RunGenerator("""
            using Pragmatic.Authorization;

            [assembly: Permission(TestApp.Billing.PermissionNames.ReadInvoices, "Read the invoices")]

            namespace TestApp.Billing;

            public static class PermissionNames
            {
                public const string ReadInvoices = "billing.invoices.read";
            }
            """);

        Registry(result).Should().NotBeNull().And.Contain("\"billing.invoices.read\"");
    }

    [Fact]
    public void RolePermissionsFromConstants_AreResolved()
    {
        // The const value must not appear anywhere else in the source. Asserting on a string the
        // permission declaration also produces would pass whether or not the role's list was read —
        // it did, until a mutation of the list path changed nothing.
        var result = RunGenerator("""
            using Pragmatic.Authorization;
            using System.Collections.Generic;

            [assembly: Permission("billing.invoices.read", "Read the invoices")]

            namespace TestApp.Billing;

            public static class PermissionNames
            {
                public const string ExportInvoices = "billing.invoices.export";
            }

            public sealed record Accountant : IRole
            {
                public static string Name => "accountant";
                public static IReadOnlyList<string> DefaultPermissions => [PermissionNames.ExportInvoices];
            }
            """);

        var registry = Registry(result);
        registry.Should().NotBeNull().And.Contain("accountant");
        registry.Should().Contain("billing.invoices.export");
    }

    /// <summary>
    ///     <c>PRAG1003</c> (EmptyRoleName) had a live validation method, but the transform dropped the role
    ///     before validation could ever run.
    /// </summary>
    [Fact]
    public void RoleWithAnUnresolvableName_ReportsPrag1003()
    {
        var result = RunGenerator("""
            using Pragmatic.Authorization;
            using System;
            using System.Collections.Generic;

            namespace TestApp.Billing;

            public sealed class Accountant : IRole
            {
                public static string Name => string.Concat("account", "ant");
                public static IReadOnlyList<string> DefaultPermissions => [];
            }
            """);

        HasDiagnostic(result, "PRAG1003").Should().BeTrue();
    }

    /// <summary>
    ///     PRAG1001 — two declarations claiming the same permission value.
    /// </summary>
    /// <remarks>
    ///     The catalogue is a dictionary keyed by name, so the second declaration is dropped and only
    ///     one of the two descriptions ever appears in the registry — which one depends on declaration
    ///     order. The diagnostic was declared and validated for, and no test asserted it.
    /// </remarks>
    [Fact]
    public void TwoPermissionsWithTheSameName_ReportsPrag1001()
    {
        var result = RunGenerator("""
            using Pragmatic.Authorization;

            [assembly: Permission("billing.invoices.read", "Read the invoices")]
            [assembly: Permission("billing.invoices.read", "Browse the invoices")]

            namespace TestApp.Billing;
            """);

        HasDiagnostic(result, "PRAG1001").Should().BeTrue(
            "one of the two never reaches the catalogue, and which one depends on declaration order");
    }

    /// <summary>The control: two distinct names, and nothing is reported.</summary>
    [Fact]
    public void TwoPermissionsWithDistinctNames_ReportNothing()
    {
        var result = RunGenerator("""
            using Pragmatic.Authorization;

            [assembly: Permission("billing.invoices.read", "Read the invoices")]
            [assembly: Permission("billing.invoices.export", "Export the invoices")]

            namespace TestApp.Billing;
            """);

        HasDiagnostic(result, "PRAG1001").Should().BeFalse();
    }

    [Fact]
    public void AWellFormedPermission_ReportsNothing()
    {
        var result = RunGenerator("""
            using Pragmatic.Authorization;

            [assembly: Permission("billing.invoices.read", "Read the invoices")]

            namespace TestApp.Billing;
            """);

        GetGeneratorDiagnostics(result).Should().BeEmpty();
    }
}
