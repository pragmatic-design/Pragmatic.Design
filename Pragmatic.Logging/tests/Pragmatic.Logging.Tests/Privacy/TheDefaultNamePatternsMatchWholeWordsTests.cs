using Pragmatic.Logging.Attributes;
using Pragmatic.Logging.Privacy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
///     The property-name patterns the framework ships match a sensitive term as a word of the name, not as
///     a run of letters inside another word.
/// </summary>
/// <remarks>
///     <para>
///         The patterns were unanchored substrings compiled case-insensitively: <c>.*ssn.*</c> masked
///         "Proce<b>ssN</b>ame", <c>.*pin.*</c> masked "Ship<b>pin</b>g", <c>.*ip.*</c> masked "Descr<b>ip</b>tion",
///         <c>.*pan.*</c> masked "Com<b>pan</b>y", <c>.*age.*</c> masked "Mess<b>age</b>". Over-masking, not a
///         leak, but it hides values an operator needs.
///     </para>
///     <para>
///         The second half of each set is the control: every name the patterns exist for is still masked,
///         including the all-lowercase compounds a word rule alone would miss.
///     </para>
/// </remarks>
public class TheDefaultNamePatternsMatchWholeWordsTests
{
    [Theory]
    [InlineData("ProcessName")]
    [InlineData("Shipping")]
    [InlineData("Mapping")]
    [InlineData("Author")]
    [InlineData("Monkey")]
    [InlineData("KeyboardLayout")]
    public void Default_DoesNotMaskAHarmlessName(string name)
        => Masks(PragmaticDataRedactorConfiguration.CreateDefault(), name).Should().BeFalse($"'{name}' is not sensitive");

    [Theory]
    [InlineData("Ssn")]
    [InlineData("CustomerSsn")]
    [InlineData("CustomerSSN")]
    [InlineData("ApiKey")]
    [InlineData("APIKey")]
    [InlineData("api_key")]
    [InlineData("apikey")]
    [InlineData("privatekey")]
    [InlineData("Pin")]
    [InlineData("UserPin")]
    [InlineData("AuthToken")]
    [InlineData("AccountNumber")]
    [InlineData("Password")]
    [InlineData("accesstoken")]
    [InlineData("CreditCard")]
    [InlineData("ClientSecret")]
    public void Default_StillMasksWhatItIsFor(string name)
        => Masks(PragmaticDataRedactorConfiguration.CreateDefault(), name).Should().BeTrue($"'{name}' is sensitive");

    [Theory]
    [InlineData("Message")]
    [InlineData("Language")]
    [InlineData("Page")]
    public void Gdpr_DoesNotMaskAHarmlessName(string name)
        => Masks(PragmaticDataRedactorConfiguration.CreateGdprCompliant(), name).Should().BeFalse($"'{name}' is not sensitive");

    [Theory]
    [InlineData("Age")]
    [InlineData("CustomerAge")]
    [InlineData("FirstName")]
    public void Gdpr_StillMasksWhatItIsFor(string name)
        => Masks(PragmaticDataRedactorConfiguration.CreateGdprCompliant(), name).Should().BeTrue($"'{name}' is sensitive");

    [Theory]
    [InlineData("Shipping")]
    [InlineData("Description")]
    [InlineData("Recipient")]
    [InlineData("Monkey")]
    [InlineData("Author")]
    public void ProductionTemplate_DoesNotMaskAHarmlessName(string name)
        => Masks(ComplianceTemplates.CreateProductionDefault().PropertyNamePatterns, name).Should().BeFalse($"'{name}' is not sensitive");

    [Theory]
    [InlineData("IpAddress")]
    [InlineData("ClientIP")]
    [InlineData("ApiKey")]
    [InlineData("AuthToken")]
    [InlineData("Password")]
    public void ProductionTemplate_StillMasksWhatItIsFor(string name)
        => Masks(ComplianceTemplates.CreateProductionDefault().PropertyNamePatterns, name).Should().BeTrue($"'{name}' is sensitive");

    [Theory]
    [InlineData("Company")]
    [InlineData("Span")]
    [InlineData("TrackingNumber")]
    public void PciDss_DoesNotMaskAHarmlessName(string name)
        => Masks(CompliancePatterns.PciDss.PropertyNamePatterns, name).Should().BeFalse($"'{name}' is not sensitive");

    [Theory]
    [InlineData("Pan")]
    [InlineData("CardPan")]
    [InlineData("TrackData")]
    [InlineData("Cvv")]
    public void PciDss_StillMasksWhatItIsFor(string name)
        => Masks(CompliancePatterns.PciDss.PropertyNamePatterns, name).Should().BeTrue($"'{name}' is sensitive");

    [Theory]
    [InlineData("Adobe")]
    [InlineData("ProcessName")]
    public void Hipaa_DoesNotMaskAHarmlessName(string name)
        => Masks(CompliancePatterns.Hipaa.PropertyNamePatterns, name).Should().BeFalse($"'{name}' is not sensitive");

    [Theory]
    [InlineData("Dob")]
    [InlineData("PatientDob")]
    [InlineData("Mrn")]
    [InlineData("Ssn")]
    public void Hipaa_StillMasksWhatItIsFor(string name)
        => Masks(CompliancePatterns.Hipaa.PropertyNamePatterns, name).Should().BeTrue($"'{name}' is sensitive");

    [Theory]
    [InlineData("Unzip")]
    [InlineData("ProcessName")]
    public void GdprPatterns_DoNotMaskAHarmlessName(string name)
        => Masks(CompliancePatterns.Gdpr.PropertyNamePatterns, name).Should().BeFalse($"'{name}' is not sensitive");

    [Theory]
    [InlineData("Zip")]
    [InlineData("ZipCode")]
    [InlineData("Ssn")]
    public void GdprPatterns_StillMaskWhatTheyAreFor(string name)
        => Masks(CompliancePatterns.Gdpr.PropertyNamePatterns, name).Should().BeTrue($"'{name}' is sensitive");

    private static bool Masks(PragmaticDataRedactorConfiguration configuration, string name)
    {
        configuration.EnableSecretDetection = false;
        return new PragmaticDataRedactor(configuration).ShouldRedact(name, PropertyCharacteristics.None);
    }

    private static bool Masks(string[] patterns, string name)
        => Masks(new PragmaticDataRedactorConfiguration { PropertyNamePatterns = patterns }, name);
}
