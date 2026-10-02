using System.Runtime.InteropServices;
using System.Text;

namespace Pragmatic.Imaging.Native;

/// <summary>
/// Retrieves error messages from the Rust native library's thread-local error store.
/// </summary>
internal static class NativeError
{
    private const int MaxErrorLength = 4096;

    /// <summary>
    /// Get the last error message from the native library.
    /// Returns null if no error is stored.
    /// </summary>
    internal static unsafe string? GetLastError()
    {
        // Use stackalloc to avoid an unmanaged heap allocation on every error check.
        byte* buf = stackalloc byte[MaxErrorLength];
        var written = NativeImports.pragmatic_last_error((nint)buf, (nuint)MaxErrorLength);
        if (written <= 0) return null;

        return Marshal.PtrToStringUTF8((nint)buf);
    }

    /// <summary>
    /// Throws <see cref="ImagingException"/> with the last native error message,
    /// or with the provided fallback message.
    /// </summary>
    internal static void ThrowLastError(string fallbackMessage = "Native imaging operation failed")
    {
        var error = GetLastError() ?? fallbackMessage;
        throw new ImagingException(error);
    }

    /// <summary>
    /// Checks the return code. If negative, throws with the native error message,
    /// classifying the failure via the operation name.
    /// </summary>
    internal static void CheckResult(int returnCode, string operation)
    {
        if (returnCode < 0)
        {
            var error = GetLastError() ?? $"{operation} failed with code {returnCode}";
            throw new ImagingException(error, ClassifyReason(operation));
        }
    }

    // Map the native operation label to a coarse failure category so callers can branch
    // on ImagingException.Reason without parsing messages.
    private static ImagingError ClassifyReason(string operation) => operation switch
    {
        _ when operation.Contains("decode", StringComparison.Ordinal)
            || operation.Contains("info", StringComparison.Ordinal) => ImagingError.DecodeFailed,
        _ when operation.Contains("encode", StringComparison.Ordinal) => ImagingError.EncodeFailed,
        _ => ImagingError.NativeError,
    };
}
