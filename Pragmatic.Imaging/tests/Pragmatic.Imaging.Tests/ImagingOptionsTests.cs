using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class ImagingOptionsTests
{
    [Fact]
    public void Default_HasSafeLimits()
    {
        var opts = ImagingOptions.Default;
        opts.MaxInputBytes.Should().Be(100 * 1024 * 1024);
        opts.MaxMegapixels.Should().Be(100);
    }

    [Fact]
    public void Strict_HasTighterLimits()
    {
        var opts = ImagingOptions.Strict;
        opts.MaxInputBytes.Should().Be(20 * 1024 * 1024);
        opts.MaxMegapixels.Should().Be(25);
    }

    [Fact]
    public void Relaxed_HasHigherLimits()
    {
        var opts = ImagingOptions.Relaxed;
        opts.MaxInputBytes.Should().Be(500 * 1024 * 1024);
        opts.MaxMegapixels.Should().Be(500);
    }

    [NativeRequiredFact]
    public void Load_WithStrictOptions_SmallImage_Works()
    {
        var png = TestHelper.CreateTestPng(50, 50);
        using var pipeline = ImagePipeline.Load(png, ImagingOptions.Strict);
        pipeline.Width.Should().Be(50);
    }

    [Fact]
    public void Load_ExceedsInputLimit_ThrowsBeforeDecode()
    {
        var opts = new ImagingOptions { MaxInputBytes = 10 };
        var bigData = new byte[100];
        var act = () => ImagePipeline.Load(bigData, opts);
        act.Should().Throw<ImagingException>().WithMessage("*exceeds limit*");
    }

    [Fact]
    public void Load_Stream_ExceedsLimit_ThrowsDuringRead()
    {
        var opts = new ImagingOptions { MaxInputBytes = 50 };
        var bigData = new byte[100];
        using var ms = new MemoryStream(bigData);
        var act = () => ImagePipeline.Load(ms, opts);
        act.Should().Throw<ImagingException>().WithMessage("*stream exceeds size limit*");
    }

    [NativeRequiredFact]
    public void Crop_OutOfBounds_ThrowsArgumentException()
    {
        var png = TestHelper.CreateTestPng(100, 100);
        using var pipeline = ImagePipeline.Load(png);
        var act = () => pipeline.Crop(50, 50, 100, 100);
        act.Should().Throw<ArgumentException>().WithMessage("*exceeds image bounds*");
    }

    [NativeRequiredFact]
    public void Resize_ZeroDimensions_ThrowsArgumentException()
    {
        var png = TestHelper.CreateTestPng(100, 100);
        using var pipeline = ImagePipeline.Load(png);
        var act = () => pipeline.Resize(0, 100);
        act.Should().Throw<ArgumentException>().WithMessage("*greater than zero*");
    }

    [NativeRequiredFact]
    public void Crop_ZeroDimensions_ThrowsArgumentException()
    {
        var png = TestHelper.CreateTestPng(100, 100);
        using var pipeline = ImagePipeline.Load(png);
        var act = () => pipeline.Crop(0, 0, 0, 50);
        act.Should().Throw<ArgumentException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void ValidateDecoded_ExactlyAtLimit_Passes()
    {
        // 1000×1000 = 1_000_000 pixels = exactly 1 MP → should pass
        var opts = new ImagingOptions { MaxMegapixels = 1 };
        var act = () => opts.ValidateDecoded(1000, 1000);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDecoded_OnePixelOverLimit_Throws()
    {
        // 1001×1000 = 1_001_000 pixels > 1_000_000 limit
        var opts = new ImagingOptions { MaxMegapixels = 1 };
        var act = () => opts.ValidateDecoded(1001, 1000);
        act.Should().Throw<ImagingException>().WithMessage("*exceeds limit*");
    }

    [Fact]
    public void ValidateDecoded_ZeroLimit_BlocksEverything()
    {
        var opts = new ImagingOptions { MaxMegapixels = 0 };
        var act = () => opts.ValidateDecoded(1, 1);
        act.Should().Throw<ImagingException>();
    }

    [Fact]
    public void ValidateInput_ExactlyAtLimit_Passes()
    {
        var opts = new ImagingOptions { MaxInputBytes = 100 };
        var act = () => opts.ValidateInput(100);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateInput_OneByteOverLimit_Throws()
    {
        var opts = new ImagingOptions { MaxInputBytes = 100 };
        var act = () => opts.ValidateInput(101);
        act.Should().Throw<ImagingException>();
    }

    // --- Typed reasons (A3) ---

    [Fact]
    public void ValidateInput_OverLimit_ReasonIsInputTooLarge()
    {
        var opts = new ImagingOptions { MaxInputBytes = 100 };
        var act = () => opts.ValidateInput(101);
        act.Should().Throw<ImagingException>().Which.Reason.Should().Be(ImagingError.InputTooLarge);
    }

    [Fact]
    public void ValidateDecoded_OverMegapixels_ReasonIsMaxMegapixelsExceeded()
    {
        var opts = new ImagingOptions { MaxMegapixels = 1 };
        var act = () => opts.ValidateDecoded(1001, 1000);
        act.Should().Throw<ImagingException>().Which.Reason.Should().Be(ImagingError.MaxMegapixelsExceeded);
    }

    [Fact]
    public void ValidateDecoded_ZeroDimensions_ReasonIsDecodeFailed()
    {
        var opts = ImagingOptions.Default;
        var act = () => opts.ValidateDecoded(0, 100);
        act.Should().Throw<ImagingException>().Which.Reason.Should().Be(ImagingError.DecodeFailed);
    }

    // --- Per-dimension caps (A1) ---

    [Fact]
    public void ValidateDecoded_ExceedsMaxWidth_Throws()
    {
        var opts = new ImagingOptions { MaxWidth = 800 };
        var act = () => opts.ValidateDecoded(801, 100);
        act.Should().Throw<ImagingException>()
            .Where(e => e.Reason == ImagingError.MaxWidthExceeded)
            .WithMessage("*width*exceeds limit*");
    }

    [Fact]
    public void ValidateDecoded_ExceedsMaxHeight_Throws()
    {
        var opts = new ImagingOptions { MaxHeight = 600 };
        var act = () => opts.ValidateDecoded(100, 601);
        act.Should().Throw<ImagingException>()
            .Which.Reason.Should().Be(ImagingError.MaxHeightExceeded);
    }

    [Fact]
    public void ValidateDecoded_WidthCapZeroMeansUnlimited()
    {
        var opts = new ImagingOptions { MaxWidth = 0, MaxHeight = 0, MaxMegapixels = 1000 };
        var act = () => opts.ValidateDecoded(20000, 1);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDecoded_WithinAllCaps_Passes()
    {
        var opts = new ImagingOptions { MaxWidth = 800, MaxHeight = 600, MaxMegapixels = 100 };
        var act = () => opts.ValidateDecoded(800, 600);
        act.Should().NotThrow();
    }

    // --- Format allow-list (A2) ---

    [Fact]
    public void ValidateFormat_NullAllowList_AcceptsAnything()
    {
        var opts = ImagingOptions.Default;
        var act = () => opts.ValidateFormat(ImageFormat.Gif);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFormat_NotInAllowList_Throws()
    {
        var opts = new ImagingOptions { AllowedFormats = [ImageFormat.Jpeg, ImageFormat.Png] };
        var act = () => opts.ValidateFormat(ImageFormat.Gif);
        act.Should().Throw<ImagingException>()
            .Where(e => e.Reason == ImagingError.FormatNotAllowed)
            .WithMessage("*not in the allowed set*");
    }

    [Fact]
    public void ValidateFormat_InAllowList_Passes()
    {
        var opts = new ImagingOptions { AllowedFormats = [ImageFormat.Jpeg, ImageFormat.Png] };
        var act = () => opts.ValidateFormat(ImageFormat.Png);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFormat_EmptyAllowList_FailsClosed()
    {
        var opts = new ImagingOptions { AllowedFormats = [] };
        var act = () => opts.ValidateFormat(ImageFormat.Png);
        act.Should().Throw<ImagingException>().Which.Reason.Should().Be(ImagingError.FormatNotAllowed);
    }
}
