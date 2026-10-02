using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>ProtectedValue</c> property of an entity is mapped to the column that stores it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>Pragmatic.Cryptography.EFCore</c> ships <c>ProtectedValueConverter</c>, and only the
///         generator can apply it. The entity generator hard-codes the conversions it emits —
///         <c>LocalizedString</c> to JSON, <c>Money</c> to its component columns — and there is no seam
///         through which an application could add one: <c>OnModelCreating</c> is an override, so a
///         partial class of the generated context cannot extend it, and there is no
///         <c>ApplyConfigurationsFromAssembly</c>. Unmapped by the generator, the property would have no
///         mapping at all, and <c>ErasureStrategy.DestroyKey</c> — declarable, validated by
///         <c>PRAG2902</c> — would be impossible to honour.
///     </para>
///     <para>
///         The conversion moves bytes and nothing else: decryption stays explicit on
///         <c>ISubjectDataProtector</c>, because reading protected data has three outcomes and one of
///         them is "this subject was erased" — which a value converter cannot say.
///     </para>
/// </remarks>
public class AProtectedValueIsStorableTests
{
    private const string Converter =
        ".HasConversion<global::Pragmatic.Cryptography.EFCore.ProtectedValueConverter>()";

    [Fact]
    public void AProtectedValueProperty_IsMappedThroughItsConverter()
    {
        var source = Render(Entity("Reason", "global::Pragmatic.Cryptography.ProtectedValue"));

        source.Should().Contain("builder.Property(e => e.Reason)").And.Contain(Converter);
    }

    /// <summary>The simple name too, which is how a source file usually spells it.</summary>
    [Fact]
    public void TheTypeIsRecognisedHoweverTheModelSpellsIt()
    {
        Render(Entity("Reason", "ProtectedValue")).Should().Contain(Converter);
        Render(Entity("Reason", "Pragmatic.Cryptography.ProtectedValue")).Should().Contain(Converter);
    }

    /// <summary>
    ///     The control: a property of another type is not given the converter.
    /// </summary>
    /// <remarks>
    ///     Without it, "the conversion is emitted" is satisfied by emitting it for everything, which
    ///     would fail to compile on the first string column — and a name test that matched
    ///     <c>MyProtectedValue</c> would be the same mistake <c>EndsWith("LocalizedString")</c> once
    ///     made.
    /// </remarks>
    [Fact]
    public void APropertyOfAnotherType_IsLeftAlone()
    {
        Render(Entity("Reason", "string")).Should().NotContain(Converter);
        Render(Entity("Reason", "MyProtectedValue")).Should().NotContain(Converter);
    }

    /// <summary>A nullable one is the same type and is mapped the same way.</summary>
    [Fact]
    public void ANullableProtectedValue_IsMappedToo()
    {
        Render(Entity("Reason", "global::Pragmatic.Cryptography.ProtectedValue?")).Should().Contain(Converter);
    }

    /// <summary>
    ///     The schema says the column is binary, so the migration and the EF model describe the same
    ///     one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Two generated surfaces, one fact: the entity configuration converts to <c>byte[]</c> and
    ///     the schema has to map to the provider's binary type. A schema that said anything else would
    ///     produce a migration the EF model does not fit, and the failure would arrive at the first
    ///     write rather than at build time.
    /// </remarks>
    [Fact]
    public void TheSchemaMapsItToTheProvidersBinaryColumn()
    {
        Sql("global::Pragmatic.Cryptography.ProtectedValue", EfCoreProvider.PostgreSql).Should().Be("bytea");
        Sql("ProtectedValue", EfCoreProvider.SqlServer).Should().Be("varbinary(max)");
        Sql("Pragmatic.Cryptography.ProtectedValue?", EfCoreProvider.Sqlite).Should().Be("BLOB");
    }

    /// <summary>The control: another type is not turned into a binary column.</summary>
    [Fact]
    public void AnotherTypeIsNotBinary()
        => Sql("string", EfCoreProvider.PostgreSql).Should().NotBe("bytea");

    /// <summary>
    ///     Without <c>Pragmatic.Cryptography.EFCore</c> the declaration is <b>reported</b>, not left
    ///     to fail as a CS0234 inside generated source.
    /// </summary>
    /// <remarks>
    ///     An error rather than a warning: there is no degraded mapping to fall back to. The property
    ///     would have none at all, which is how <c>ErasureStrategy.DestroyKey</c> came to be
    ///     declarable, validated by <c>PRAG2902</c>, and impossible to honour.
    /// </remarks>
    [Fact]
    public void WithoutThePackage_TheDeclarationIsReported()
    {
        var source = Stubs + """

            namespace App
            {
                [Pragmatic.Persistence.Entity]
                public partial class LeaveRequest
                {
                    public System.Guid Id { get; set; }
                    public Pragmatic.Cryptography.ProtectedValue Reason { get; private set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(source + EntryPoint);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0652").Should().BeTrue();
    }

    /// <summary>The control: an entity with no protected value is not told to reference anything.</summary>
    [Fact]
    public void WithoutAProtectedValue_NothingIsReported()
    {
        var source = Stubs + """

            namespace App
            {
                [Pragmatic.Persistence.Entity]
                public partial class LeaveRequest
                {
                    public System.Guid Id { get; set; }
                    public string Reason { get; private set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(source + EntryPoint);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0652").Should().BeFalse();
    }

    /// <summary>
    ///     The smallest compilation in which the generator's persistence feature runs, plus the type
    ///     whose storage this is about.
    /// </summary>
    private const string Stubs = """
        namespace Pragmatic.Persistence
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }
        namespace Pragmatic.Cryptography
        {
            public readonly record struct ProtectedValue(byte[] Packed);
        }
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    ///     What makes the compilation a host, which is where the entity configuration is generated and
    ///     therefore where the missing package is reported.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A boundary library declares the entity and takes <c>Pragmatic.Cryptography</c> for the
    ///     type; which package stores it is the host's decision. Reported in the module, this refused a
    ///     correct layout — Time off's, where the module has the type and the host has the storage.
    /// </remarks>
    private const string EntryPoint = """

        internal static class Program
        {
            private static void Main() { }
        }
        """;

    private static string Sql(string typeName, EfCoreProvider provider)
        => SqlTypeMapper.MapToSqlType(typeName, provider);

    private static string Render(EntityMetadataModel model)
        => new EntityConfigurationTemplate(model).RenderOutput().Text;

    private static EntityMetadataModel Entity(string propertyName, string typeName)
        => new()
        {
            TypeName = "LeaveRequest",
            FullTypeName = "TimeOff.Leave.Entities.LeaveRequest",
            Namespace = "TimeOff.Leave.Entities",
            IdType = "Guid",
            IsValid = true,
            Properties = new[]
            {
                new PropertyMetadataModel { Name = propertyName, TypeName = typeName, IsNullable = true },
            }.ToEquatableArray(),
        };
}
