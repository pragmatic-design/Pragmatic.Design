using System.Runtime.InteropServices;
using Pragmatic.Testing.Visual;
using SkiaSharp;

namespace Pragmatic.VisualTesting.Tests;

/// <summary>The committed references, decoded by SkiaSharp.</summary>
internal static class Fixtures
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "References");

    public static RgbaImage Load(string file)
    {
        using var data = SKData.Create(Path.Combine(Directory, file)) ?? throw new FileNotFoundException(file);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException($"Skia cannot read {file}.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidOperationException($"Skia decode of {file}: {result}.");

        var pixels = new byte[info.BytesSize];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new RgbaImage(info.Width, info.Height, pixels);
    }
}
