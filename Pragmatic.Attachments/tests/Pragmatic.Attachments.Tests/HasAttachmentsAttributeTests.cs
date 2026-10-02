namespace Pragmatic.Attachments.Tests;

/// <summary>
///     Unit tests for <see cref="HasAttachmentsAttribute"/>. Its defaults are the contract the
///     generated upload action enforces, so they are pinned here rather than only in SG snapshots.
/// </summary>
public class HasAttachmentsAttributeTests
{
    [Fact]
    public void Default_MaxPerEntity_Is20()
        => new HasAttachmentsAttribute().MaxPerEntity.Should().Be(20);

    [Fact]
    public void Default_MaxFileSizeBytes_Is10Megabytes()
        => new HasAttachmentsAttribute().MaxFileSizeBytes.Should().Be(10_485_760);

    [Fact]
    public void Default_AllowedExtensions_IsEmpty()
        => new HasAttachmentsAttribute().AllowedExtensions.Should().BeEmpty();

    [Fact]
    public void Default_Container_IsNull()
        => new HasAttachmentsAttribute().Container.Should().BeNull();

    [Fact]
    public void Default_SubBoundary_IsNull()
        => new HasAttachmentsAttribute().SubBoundary.Should().BeNull();

    [Fact]
    public void Default_PurgeDeletedAfterDays_IsZero_MeaningNoPurge()
        => new HasAttachmentsAttribute().PurgeDeletedAfterDays.Should().Be(0);

    [Fact]
    public void Default_PurgeCron_IsNightlyAt3()
        => new HasAttachmentsAttribute().PurgeCron.Should().Be("0 3 * * *");

    [Fact]
    public void CustomValues_AreApplied()
    {
        var attribute = new HasAttachmentsAttribute
        {
            MaxPerEntity = 5,
            MaxFileSizeBytes = 25_000_000,
            AllowedExtensions = ".pdf,.docx",
            Container = "contracts",
            SubBoundary = "ContractFiles",
            PurgeDeletedAfterDays = 90,
            PurgeCron = "0 4 * * 0",
        };

        attribute.MaxPerEntity.Should().Be(5);
        attribute.MaxFileSizeBytes.Should().Be(25_000_000);
        attribute.AllowedExtensions.Should().Be(".pdf,.docx");
        attribute.Container.Should().Be("contracts");
        attribute.SubBoundary.Should().Be("ContractFiles");
        attribute.PurgeDeletedAfterDays.Should().Be(90);
        attribute.PurgeCron.Should().Be("0 4 * * 0");
    }

    [Fact]
    public void Type_IsSealed()
        => typeof(HasAttachmentsAttribute).IsSealed.Should().BeTrue();

    [Fact]
    public void AttributeUsage_TargetsClassOnly()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(HasAttachmentsAttribute), typeof(AttributeUsageAttribute))!;

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void AttributeUsage_IsNotInherited()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(HasAttachmentsAttribute), typeof(AttributeUsageAttribute))!;

        usage.Inherited.Should().BeFalse();
    }

    [Fact]
    public void Applied_AsAttribute_RoundTripsNamedArguments()
    {
        var attribute = (HasAttachmentsAttribute)Attribute.GetCustomAttribute(
            typeof(AnnotatedOrder), typeof(HasAttachmentsAttribute))!;

        attribute.MaxPerEntity.Should().Be(3);
        attribute.AllowedExtensions.Should().Be("pdf");
        attribute.PurgeDeletedAfterDays.Should().Be(30);
    }

    [HasAttachments(MaxPerEntity = 3, AllowedExtensions = "pdf", PurgeDeletedAfterDays = 30)]
    private sealed class AnnotatedOrder;
}
