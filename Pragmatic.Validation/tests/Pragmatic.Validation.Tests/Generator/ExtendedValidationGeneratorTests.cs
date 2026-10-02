using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for extended validation attributes: Guid, ValidEnum, FutureDate, PastDate, OneOf.
/// </summary>
public class ExtendedValidationGeneratorTests : ValidationGeneratorTestBase
{
    [Fact]
    public void Generator_WithGuidAttribute_GeneratesGuidCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record AssignTaskRequest
                              {
                                  [Guid]
                                  public string UserId { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("GuidAttribute.IsValidGuid");
        generatedSource.Should().Contain("\"validation.guid\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithGuidAndRequired_GeneratesCorrectOrder()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record AssignTaskRequest
                              {
                                  [Required]
                                  [Guid]
                                  public string UserId { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("string.IsNullOrEmpty(UserId)");
        generatedSource.Should().Contain("GuidAttribute.IsValidGuid");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithValidEnumAttribute_GeneratesEnumCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public enum OrderStatus { Active, Inactive, Suspended }

                              public partial record UpdateStatusRequest
                              {
                                  [ValidEnum]
                                  public OrderStatus Status { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("ValidEnumAttribute.IsValidEnum");
        generatedSource.Should().Contain("\"validation.enum\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithFutureDateAttribute_GeneratesDateTimeCheck()
    {
        const string source = """
                              using System;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ScheduleRequest
                              {
                                  [FutureDate]
                                  public DateTime StartDate { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("ValidationTimeProvider.Current.GetUtcNow().UtcDateTime");
        generatedSource.Should().Contain("\"validation.future_date\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithFutureDateAttribute_DateTimeOffset_GeneratesCorrectCheck()
    {
        const string source = """
                              using System;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ScheduleRequest
                              {
                                  [FutureDate]
                                  public DateTimeOffset StartDate { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("ValidationTimeProvider.Current.GetUtcNow()");
        generatedSource.Should().Contain("\"validation.future_date\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithPastDateAttribute_GeneratesDateTimeCheck()
    {
        const string source = """
                              using System;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreatePersonRequest
                              {
                                  [PastDate]
                                  public DateTime BirthDate { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("ValidationTimeProvider.Current.GetUtcNow().UtcDateTime");
        generatedSource.Should().Contain("\"validation.past_date\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithPastDateAttribute_DateTimeOffset_GeneratesCorrectCheck()
    {
        const string source = """
                              using System;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreatePersonRequest
                              {
                                  [PastDate]
                                  public DateTimeOffset BirthDate { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("ValidationTimeProvider.Current.GetUtcNow()");
        generatedSource.Should().Contain("\"validation.past_date\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithOneOfAttribute_GeneratesAllowedValuesCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record UpdateArticleRequest
                              {
                                  [OneOf("draft", "published", "archived")]
                                  public string Status { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("\"draft\"");
        generatedSource.Should().Contain("\"published\"");
        generatedSource.Should().Contain("\"archived\"");
        generatedSource.Should().Contain("\"validation.oneof\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithCustomCrossPropertyAttribute_EmitsPropertyValueProvider()
    {
        // A custom RequiresInstance attribute reads siblings via IPropertyValueProvider, which the
        // generated type must implement; otherwise validation throws at runtime.
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public sealed class NotSameAsAttribute : ValidationAttribute
                              {
                                  public NotSameAsAttribute(string other) => Other = other;
                                  public string Other { get; }
                                  public override string DefaultMessageKey => "validation.notsame";
                                  public override bool RequiresInstance => true;
                                  public override bool IsValid(object? value) => true;
                                  public override bool IsValid(object? value, object instance)
                                  {
                                      if (value is null) return true;
                                      var other = ((IPropertyValueProvider)instance).GetPropertyValue(Other);
                                      return !Equals(value, other);
                                  }
                              }

                              public partial record ChangeSecretRequest
                              {
                                  [Required]
                                  public string OldSecret { get; init; }

                                  [Required]
                                  [NotSameAs(nameof(OldSecret))]
                                  public string NewSecret { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("IPropertyValueProvider");
        generatedSource.Should().Contain("GetPropertyValue");
        generatedSource.Should().Contain("nameof(OldSecret) => OldSecret");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithOneOfOnNonIntNumeric_CastsLiteralsToPropertyType()
    {
        // Regression: integer literals default to `int`, so on a `long` property the generated
        // `Equals(prop, 1)` would box int-vs-long and be always false. The generator must cast.
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record LevelRequest
                              {
                                  [OneOf(1, 2, 3)]
                                  public long Level { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("(long)1");
        generatedSource.Should().Contain("(long)2");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithOneOfAndRequired_GeneratesCorrectOrder()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record UpdateArticleRequest
                              {
                                  [Required]
                                  [OneOf("draft", "published", "archived")]
                                  public string Status { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("string.IsNullOrEmpty(Status)");
        generatedSource.Should().Contain("\"draft\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithCustomMessageKey_UsesCustomKey()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record AssignTaskRequest
                              {
                                  [Guid(MessageKey = "custom.invalid_guid")]
                                  public string UserId { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("custom.invalid_guid");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithMultipleNewAttributes_GeneratesAllChecks()
    {
        const string source = """
                              using System;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public enum Priority { Low, Medium, High }

                              public partial record CreateEventRequest
                              {
                                  [Required]
                                  [Guid]
                                  public string OwnerId { get; init; }

                                  [ValidEnum]
                                  public Priority Priority { get; init; }

                                  [FutureDate]
                                  public DateTime EventDate { get; init; }

                                  [Required]
                                  [OneOf("meeting", "workshop", "conference")]
                                  public string EventType { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();

        // GUID check
        generatedSource.Should().Contain("GuidAttribute.IsValidGuid");

        // Enum check
        generatedSource.Should().Contain("ValidEnumAttribute.IsValidEnum");

        // Future date check
        generatedSource.Should().Contain("ValidationTimeProvider.Current.GetUtcNow()");

        // OneOf check
        generatedSource.Should().Contain("\"meeting\"");
        generatedSource.Should().Contain("\"workshop\"");
        generatedSource.Should().Contain("\"conference\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }
}
