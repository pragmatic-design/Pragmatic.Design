namespace Pragmatic.Imaging.Tests;

/// <summary>
/// Helper to create minimal test images using the public API.
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// Check if the native library is available on this platform.
    /// </summary>
    internal static bool IsNativeAvailable()
    {
        try
        {
            var png = CreateTestPng(2, 2);
            _ = ImageInfo.FromBytes(png);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch
        {
            return true; // DLL loaded but other error = native is available
        }
    }

    /// <summary>Create a test PNG of the given size using the public ImagePipeline API.</summary>
    internal static byte[] CreateTestPng(uint width, uint height)
    {
        // Create a small seed image first, then resize to desired dimensions.
        // Bootstrap: encode a minimal 1x1 PNG via QR (which always works), then resize.
        // Actually simpler: use QR to get any valid PNG, then resize it.
        var qrPng = QrCode.GeneratePng("test", moduleSize: 1, margin: 0);

        using var pipeline = ImagePipeline.Load(qrPng);
        pipeline.Resize(width, height, ResizeFilter.Nearest);
        return pipeline.Encode(ImageFormat.Png);
    }
}
