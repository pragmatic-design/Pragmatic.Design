using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Executes the generated mapping code (emit → load → invoke) so the RUNTIME behaviour of the
///     polymorphic dispatch ([MapDerived]) and the conditional gate ([MapCondition]) is actually
///     exercised — the module review found both were only covered by compile-time/diagnostic tests.
/// </summary>
public class RuntimeDispatchTests : MappingGeneratorTestBase
{
    private static Assembly EmitAndLoad(SourceGenRunResult result)
    {
        using var ms = new MemoryStream();
        var emit = result.OutputCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return Assembly.Load(ms.ToArray());
    }

    private static MethodInfo FromEntity(Type dtoType, Type sourceType) =>
        dtoType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "FromEntity" && m.GetParameters().Length == 1
                         && m.GetParameters()[0].ParameterType == sourceType);

    // [MapDerived] runtime polymorphic dispatch: FromEntity on the base DTO must return the
    // derived DTO instance when the runtime entity type is the derived entity.
    [Fact]
    public void MapDerived_FromEntity_DispatchesToDerivedDto()
    {
        var asm = EmitAndLoad(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace Zoo
            {
                public class Animal { public string Name { get; set; } = ""; }
                public class Dog : Animal { public string Breed { get; set; } = ""; }

                [MapFrom<Animal>]
                [MapDerived<Dog, DogDto>]
                public partial class AnimalDto { public string Name { get; init; } = ""; }

                [MapFrom<Dog>]
                public partial class DogDto : AnimalDto { public string Breed { get; init; } = ""; }
            }
            """));

        var animal = asm.GetType("Zoo.Animal")!;
        var dog = asm.GetType("Zoo.Dog")!;
        var animalDto = asm.GetType("Zoo.AnimalDto")!;

        var dogInstance = Activator.CreateInstance(dog)!;
        dog.GetProperty("Name")!.SetValue(dogInstance, "Rex");
        dog.GetProperty("Breed")!.SetValue(dogInstance, "Labrador");

        var dto = FromEntity(animalDto, animal).Invoke(null, [dogInstance])!;

        dto.GetType().Name.Should().Be("DogDto");
        dto.GetType().GetProperty("Breed")!.GetValue(dto).Should().Be("Labrador");
        dto.GetType().GetProperty("Name")!.GetValue(dto).Should().Be("Rex");
    }

    // Base entity (not the derived type) must still map to the base DTO.
    [Fact]
    public void MapDerived_FromEntity_BaseEntity_MapsToBaseDto()
    {
        var asm = EmitAndLoad(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace Zoo
            {
                public class Animal { public string Name { get; set; } = ""; }
                public class Dog : Animal { public string Breed { get; set; } = ""; }

                [MapFrom<Animal>]
                [MapDerived<Dog, DogDto>]
                public partial class AnimalDto { public string Name { get; init; } = ""; }

                [MapFrom<Dog>]
                public partial class DogDto : AnimalDto { public string Breed { get; init; } = ""; }
            }
            """));

        var animal = asm.GetType("Zoo.Animal")!;
        var animalDto = asm.GetType("Zoo.AnimalDto")!;

        var animalInstance = Activator.CreateInstance(animal)!;
        animal.GetProperty("Name")!.SetValue(animalInstance, "Generic");

        var dto = FromEntity(animalDto, animal).Invoke(null, [animalInstance])!;

        dto.GetType().Name.Should().Be("AnimalDto");
    }

    // [MapCondition] runtime gate: the property maps only when the predicate returns true;
    // when gated off it keeps the type default (null for a reference type), not the initializer.
    [Theory]
    [InlineData(true, "classified")]
    [InlineData(false, null)]
    public void MapCondition_GatesPropertyAtRuntime(bool isPublic, string? expectedSecret)
    {
        var asm = EmitAndLoad(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace Cond
            {
                public class Person { public bool IsPublic { get; set; } public string Secret { get; set; } = ""; }

                [MapFrom<Person>]
                public partial class PersonDto
                {
                    [MapCondition(nameof(ShouldMapSecret))]
                    public string Secret { get; init; } = "";

                    private static bool ShouldMapSecret(Person p) => p.IsPublic;
                }
            }
            """));

        var person = asm.GetType("Cond.Person")!;
        var personDto = asm.GetType("Cond.PersonDto")!;

        var instance = Activator.CreateInstance(person)!;
        person.GetProperty("IsPublic")!.SetValue(instance, isPublic);
        person.GetProperty("Secret")!.SetValue(instance, "classified");

        var dto = FromEntity(personDto, person).Invoke(null, [instance])!;

        dto.GetType().GetProperty("Secret")!.GetValue(dto).Should().Be(expectedSecret);
    }

    // Enum → different enum type maps by member name at runtime.
    [Fact]
    public void EnumToEnum_MapsByMemberNameAtRuntime()
    {
        var asm = EmitAndLoad(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace En
            {
                public enum SourceStatus { Draft, Active, Archived }
                public enum TargetStatus { Draft, Active, Archived }

                public class Doc { public SourceStatus Status { get; set; } }

                [MapFrom<Doc>]
                public partial class DocDto { public TargetStatus Status { get; init; } }
            }
            """));

        var doc = asm.GetType("En.Doc")!;
        var docDto = asm.GetType("En.DocDto")!;
        var sourceStatus = asm.GetType("En.SourceStatus")!;

        var instance = Activator.CreateInstance(doc)!;
        doc.GetProperty("Status")!.SetValue(instance, Enum.Parse(sourceStatus, "Archived"));

        var dto = FromEntity(docDto, doc).Invoke(null, [instance])!;

        dto.GetType().GetProperty("Status")!.GetValue(dto)!.ToString().Should().Be("Archived");
    }

    // [MapTo] ToEntity executed at runtime, not only compiled.
    [Fact]
    public void MapTo_ToEntity_BuildsEntityAtRuntime()
    {
        var asm = EmitAndLoad(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace W
            {
                public class Widget { public string Name { get; set; } = ""; public int Size { get; set; } }

                [MapTo<Widget>]
                public partial record WidgetDto(string Name, int Size);
            }
            """));

        var widgetDto = asm.GetType("W.WidgetDto")!;
        var dtoInstance = Activator.CreateInstance(widgetDto, "gadget", 42)!;

        var entity = widgetDto.GetMethod("ToEntity")!.Invoke(dtoInstance, null)!;

        entity.GetType().GetProperty("Name")!.GetValue(entity).Should().Be("gadget");
        entity.GetType().GetProperty("Size")!.GetValue(entity).Should().Be(42);
    }

    // [MapConstructor] selects the marked constructor at runtime.
    [Fact]
    public void MapConstructor_UsesMarkedConstructorAtRuntime()
    {
        var asm = EmitAndLoad(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace Mc
            {
                public class Product
                {
                    public Product() { }
                    [MapConstructor]
                    public Product(string sku) { Sku = sku; ViaCtor = true; }
                    public string Sku { get; set; } = "";
                    public string Name { get; set; } = "";
                    public bool ViaCtor { get; set; }
                }

                [MapTo<Product>]
                public partial record ProductDto(string Sku, string Name);
            }
            """));

        var productDto = asm.GetType("Mc.ProductDto")!;
        var dtoInstance = Activator.CreateInstance(productDto, "SKU-1", "Widget")!;

        var entity = productDto.GetMethod("ToEntity")!.Invoke(dtoInstance, null)!;

        entity.GetType().GetProperty("ViaCtor")!.GetValue(entity).Should().Be(true); // marked ctor ran
        entity.GetType().GetProperty("Sku")!.GetValue(entity).Should().Be("SKU-1");
        entity.GetType().GetProperty("Name")!.GetValue(entity).Should().Be("Widget");
    }

    // A cycle through a Dictionary VALUE type is detected, so FromEntity gets the visited-set
    // plumbing instead of recursing unbounded on cyclic data.
    [Fact]
    public void CircularReference_ThroughDictionaryValue_IsDetected()
    {
        var result = RunGenerator("""
            using System.Collections.Generic;
            using Pragmatic.Mapping.Attributes;
            namespace Cyc
            {
                public class Node { public string Name { get; set; } = ""; public Dictionary<string, Node> Children { get; set; } = new(); }

                [MapFrom<Node>]
                public partial class NodeDto { public string Name { get; init; } = ""; public Dictionary<string, NodeDto> Children { get; init; } = new(); }
            }
            """);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var mapping = GetGeneratedSource(result, "NodeDto.Mapping");
        mapping.Should().Contain("visited"); // cycle detected → instance-tracking generated
    }
}
