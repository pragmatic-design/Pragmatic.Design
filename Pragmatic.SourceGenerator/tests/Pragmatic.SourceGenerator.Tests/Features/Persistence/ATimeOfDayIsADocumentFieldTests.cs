using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>TimeOnly</c>, <c>DateOnly</c> and <c>TimeSpan</c> members of an entity the documents engine persists are fields (#476), required and
///     optional. Before, PRAG3104 refused them, as it refused the Showcase's <c>Property.CheckInTime</c>.
/// </summary>
/// <remarks>The bridge is declared by the types the generator recognises it by, as in <see cref="ALocalizedStringIsADocumentFieldTests" />.</remarks>
public class ATimeOfDayIsADocumentFieldTests
{
    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    private const string Source = """
        using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Stays
        {
            [Entity]
            public partial class Stay : IEntity
            {
                public TimeOnly CheckIn { get; set; }
                public TimeOnly? LateCheckOut { get; set; }
                public DateOnly Arrival { get; set; }
                public DateOnly? Departure { get; set; }
                public TimeSpan Length { get; set; }
                public TimeSpan? Grace { get; set; }
            }
        }
        """;

    [Fact]
    public void TheTimeOfDayTheDayAndTheDuration_AreFields()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Source + DocumentsBridge, "Pragmatic.Persistence.Documents.Generated",
            [
                GeneratorTestHelper.FromType<EntityAttribute>(),
                GeneratorTestHelper.FromType<IEntity>(),
                GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
                GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
            ]);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3104").Should().BeEmpty("TimeOnly, DateOnly and TimeSpan are document fields");
        var generated = string.Concat(GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values);
        generated.Should().Contain("DocMapping.ReadTimeOnly(", "a TimeOnly is read back as one");
        generated.Should().Contain("DocMapping.ReadDateOnly(", "a DateOnly is read back as one");
        generated.Should().Contain("DocMapping.ReadTimeSpan(", "a TimeSpan is read back as one");
        generated.Should().Contain("DocJson.ReadTimeOnly(", "and from Cosmos DB's JSON alike");
    }
}
