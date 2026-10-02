// =============================================================================
// Pragmatic.Patch - PatchGeneratorTests
// Tests for the Patch source generator
// =============================================================================

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Patch.Tests.Generator;

public class PatchGeneratorTests : PatchGeneratorTestBase
{

    // ═══════════════════════════════════════════════════════════════════════
    // Happy Path
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_SimpleEntity_GeneratesOptionalProperties()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; set; }
                public string LastName { get; set; }
                public string? Email { get; set; }
                public int Age { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("Optional<string>");
        generated.Should().Contain("Optional<int>");
        generated.Should().Contain("FirstName");
        generated.Should().Contain("LastName");
        generated.Should().Contain("Email");
        generated.Should().Contain("Age");
    }

    [Fact]
    public void GeneratePatch_SimpleEntity_GeneratesApplyToMethod()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; set; }
                public string LastName { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("void ApplyTo(");
        generated.Should().Contain("Guest");
        generated.Should().Contain("if (FirstName.HasValue)");
        generated.Should().Contain("entity.FirstName = FirstName.Value!;");
    }

    [Fact]
    public void GeneratePatch_SimpleEntity_GeneratesModifiedProperties()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; set; }
                public string LastName { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("IReadOnlySet<string> ModifiedProperties");
        generated.Should().Contain("GetModifiedProperties()");
        generated.Should().Contain("set.Add(\"FirstName\")");
        generated.Should().Contain("set.Add(\"LastName\")");
    }

    [Fact]
    public void GeneratePatch_SimpleEntity_GeneratesJsonConverter()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; set; }
                public string LastName { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var converter = GetGeneratedSource(result, "UpdateGuestPatch.JsonConverter");
        converter.Should().NotBeNull();
        converter.Should().Contain("[JsonConverter(typeof(UpdateGuestPatchJsonConverter))]");
        converter.Should().Contain("class UpdateGuestPatchJsonConverter");
        converter.Should().Contain("JsonConverter<UpdateGuestPatch>");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Setters → SetXxx()
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_EntityWithPrivateSetters_UsesSetMethods()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; private set; }
                public string LastName { get; private set; }

                public void SetFirstName(string value) => FirstName = value;
                public void SetLastName(string value) => LastName = value;
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("entity.SetFirstName(FirstName.Value!);");
        generated.Should().Contain("entity.SetLastName(LastName.Value!);");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Excluded Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_ExcludesIdAndAuditProperties()
    {
        var source = """
            using Pragmatic.Patch.Attributes;
            using System;

            namespace TestApp;

            public class Guest
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get; set; }
                public string FirstName { get; set; }
                public DateTime CreatedAt { get; set; }
                public string CreatedBy { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string UpdatedBy { get; set; }
                public bool IsDeleted { get; set; }
                public uint RowVersion { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("FirstName");
        generated.Should().NotContain("Optional<global::System.Guid> Id ");
        generated.Should().NotContain("PersistenceId");
        generated.Should().NotContain("CreatedAt");
        generated.Should().NotContain("CreatedBy");
        generated.Should().NotContain("UpdatedAt");
        generated.Should().NotContain("UpdatedBy");
        generated.Should().NotContain("IsDeleted");
        generated.Should().NotContain("RowVersion");
    }

    [Fact]
    public void GeneratePatch_ExcludesNavigationProperties()
    {
        var source = """
            using Pragmatic.Patch.Attributes;
            using System;
            using System.Collections.Generic;

            namespace TestApp;

            public class Address
            {
                public Guid Id { get; set; }
                public string Street { get; set; }
            }

            public class Guest
            {
                public string FirstName { get; set; }
                public ICollection<Address> Addresses { get; set; }
                public Address PrimaryAddress { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("FirstName");
        generated.Should().NotContain("Addresses");
        generated.Should().NotContain("PrimaryAddress");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_NotPartial_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; set; }
            }

            [GeneratePatch<Guest>]
            public record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        // PRAG2200 is the companion analyzer's, on the declaration.
        HasDiagnostic(result, "PRAG2200").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse("nothing was generated into a type that cannot take it");
    }

    [Fact]
    public void GeneratePatch_NoSettableProperties_ReportsDiagnostic()
    {
        // Entity has only excluded properties (Id, PersistenceId, IsDeleted, RowVersion)
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public int Id { get; set; }
                public int PersistenceId { get; set; }
                public bool IsDeleted { get; set; }
                public string RowVersion { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG2202").Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Value Types
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_ValueTypes_GeneratesCorrectOptionalWrappers()
    {
        var source = """
            using Pragmatic.Patch.Attributes;
            using System;

            namespace TestApp;

            public class Guest
            {
                public string Name { get; set; }
                public int Age { get; set; }
                public DateTime? BirthDate { get; set; }
                public decimal Balance { get; set; }
                public bool IsVip { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "UpdateGuestPatch.Patch");
        generated.Should().NotBeNull();
        generated.Should().Contain("Optional<string>");
        generated.Should().Contain("Optional<int>");
        generated.Should().Contain("Optional<decimal>");
        generated.Should().Contain("Optional<bool>");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRAG2201 — Unresolvable entity type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_UnresolvableEntityType_ReportsPRAG2201()
    {
        // The generic argument references a type that does not exist in the compilation.
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            [GeneratePatch<NonExistentEntity>]
            public partial record UpdateNonExistentPatch;
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG2201").Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // camelCase / PascalCase dual JSON support
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePatch_SimpleEntity_GeneratedConverterHandlesBothCamelAndPascalCaseKeys()
    {
        var source = """
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Guest
            {
                public string FirstName { get; set; }
            }

            [GeneratePatch<Guest>]
            public partial record UpdateGuestPatch;
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var converter = GetGeneratedSource(result, "UpdateGuestPatch.JsonConverter");
        converter.Should().NotBeNull();
        // The generated converter must handle both camelCase ("firstName") and PascalCase ("FirstName").
        converter.Should().Contain("firstName");
        converter.Should().Contain("FirstName");
    }
}
