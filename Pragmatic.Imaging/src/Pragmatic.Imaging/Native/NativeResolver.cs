using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Native;

/// <summary>
/// Installs a hardened <see cref="NativeLibrary"/> import resolver for this assembly's native
/// dependency, restricting the DLL search to application-local and system directories. This
/// mitigates DLL hijacking via user-writable <c>PATH</c> entries on Windows.
/// </summary>
/// <remarks>
/// Invoked from the <see cref="NativeImports"/> static constructor, which the CLR runs exactly once
/// before the first P/Invoke — so the hardening is active for the static/fluent APIs
/// (<see cref="ImagePipeline"/>, <see cref="QrCode"/>, …) too, not only when <c>AddPragmaticImaging</c>
/// is called, and registration is inherently thread-safe and idempotent.
/// </remarks>
internal static class NativeResolver
{
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeImports).Assembly, static (libraryName, assembly, searchPath) =>
        {
            // Restrict to application-local and system directories; skip user-writable PATH entries.
            var safePath = searchPath ?? (DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32);
            NativeLibrary.TryLoad(libraryName, assembly, safePath, out var handle);
            return handle;
        });
    }
}
