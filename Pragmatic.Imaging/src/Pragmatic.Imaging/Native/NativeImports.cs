using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Native;

/// <summary>
/// P/Invoke declarations for the pragmatic_native Rust library.
/// All functions return 0 on success, negative on error.
/// </summary>
internal static partial class NativeImports
{
    private const string LibName = "Pragmatic.Imaging.Native";

    // Runs once before the first P/Invoke (CLR guarantee), installing the hardened DLL-import
    // resolver for the static/fluent APIs as well as the DI path.
    static NativeImports() => NativeResolver.Register();

    // --- Error ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int pragmatic_last_error(nint buf, nuint buf_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void pragmatic_free_buffer(nint ptr, nuint len);

    // --- Info ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_info(
        byte* data, nuint len,
        uint* out_w, uint* out_h, int* out_format);

    // --- Decode ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_decode(
        byte* data, nuint len,
        nint* out_rgba, nuint* out_rgba_len,
        uint* out_w, uint* out_h);

    // --- Encode ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_encode(
        byte* rgba, nuint len,
        uint w, uint h,
        int format, byte quality,
        nint* out_data, nuint* out_len);

    // --- Transform ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_resize(
        byte* rgba, nuint len,
        uint src_w, uint src_h,
        uint dst_w, uint dst_h,
        int filter,
        nint* @out, nuint* out_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_crop(
        byte* rgba, nuint len,
        uint w, uint h,
        uint x, uint y, uint crop_w, uint crop_h,
        nint* @out, nuint* out_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_rotate(
        byte* rgba, nuint len,
        uint w, uint h,
        int degrees,
        nint* @out, nuint* out_len,
        uint* out_w, uint* out_h);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_flip(
        byte* rgba, nuint len,
        uint w, uint h,
        int horizontal,
        nint* @out, nuint* out_len);

    // --- Filters ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_grayscale(
        byte* rgba, nuint len, uint w, uint h,
        nint* @out, nuint* out_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_blur(
        byte* rgba, nuint len, uint w, uint h,
        float sigma,
        nint* @out, nuint* out_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_sharpen(
        byte* rgba, nuint len, uint w, uint h,
        float sigma, int threshold,
        nint* @out, nuint* out_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_brightness(
        byte* rgba, nuint len, uint w, uint h,
        int value,
        nint* @out, nuint* out_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_image_contrast(
        byte* rgba, nuint len, uint w, uint h,
        float value,
        nint* @out, nuint* out_len);

    // --- QR ---

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_qr_generate(
        byte* text, nuint text_len,
        uint size, uint margin,
        nint* out_png, nuint* out_png_len);

    /// <summary>
    /// Managed wrapper around <see cref="pragmatic_qr_generate(byte*, nuint, uint, uint, nint*, nuint*)"/>
    /// that handles UTF-8 encoding and pinning of <paramref name="text"/> internally so callers don't
    /// have to repeat the marshalling boilerplate.
    /// </summary>
    internal static unsafe int pragmatic_qr_generate(
        string text,
        uint size, uint margin,
        nint* out_png, nuint* out_png_len)
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
        fixed (byte* textPtr = textBytes)
        {
            return pragmatic_qr_generate(textPtr, (nuint)textBytes.Length, size, margin, out_png, out_png_len);
        }
    }
}
