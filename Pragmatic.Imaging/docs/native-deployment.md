# Native Deployment

`Pragmatic.Imaging` ships with a Rust-compiled native library, loaded under the name
`Pragmatic.Imaging.Native`: the file is `Pragmatic.Imaging.Native.dll` on Windows and
`libPragmatic.Imaging.Native.so` / `.dylib` on Linux / macOS. This guide covers platform coverage,
publish options, and container considerations.

---

## Supported runtimes

The NuGet package carries native binaries for the RIDs below:

| RID | File in the package | Status |
|-----|------|--------|
| `win-x64` | `Pragmatic.Imaging.Native.dll` | **Shipped** |
| `linux-x64` | `libPragmatic.Imaging.Native.so` | **Shipped** (glibc) |
| `osx-arm64` | `libPragmatic.Imaging.Native.dylib` | **Shipped** — built by the `Imaging Native` CI workflow; no test runs it on macOS yet |
| `linux-arm64`, `osx-x64` | — | Not shipped |
| `linux-musl-x64` (Alpine) | — | Not planned (glibc required) |

For a RID the package does not ship, build the library yourself (below) and put it next to your
app under the name the loader asks for — on macOS, `libPragmatic.Imaging.Native.dylib`.

---

## Framework-dependent deployment

For framework-dependent apps (the default `dotnet publish`), the native binary is picked
up automatically from the NuGet's `runtimes/` folder:

```bash
dotnet publish -c Release
```

The native binary ends up under `publish/runtimes/{rid}/native/`.

---

## Self-contained deployment

For self-contained apps, MSBuild copies the native binary for the target RID:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

---

## Native AOT

The binding is AOT-compatible — no reflection, no dynamic code generation, `LibraryImport`-generated P/Invoke.

```bash
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

AOT affects the .NET code; the Rust library still ships alongside the AOT-compiled app.

---

## Docker

The native library needs **glibc** on Linux. Recommended base images:

- `mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim` — Debian, works
- `mcr.microsoft.com/dotnet/runtime:10.0-jammy` — Ubuntu, works
- `mcr.microsoft.com/dotnet/runtime:10.0-alpine` — **does NOT work** (musl, not glibc)

### Minimal Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./src/App -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "App.dll"]
```

The package's `linux-x64` binary lands in `publish/runtimes/linux-x64/native/`; nothing else to copy.

---

## Verifying the native binary is present

The library loads lazily on first use. Probe it at startup and fail fast:

```csharp
try
{
    _ = ImageInfo.FromBytes(QrCode.GeneratePng("probe", moduleSize: 1, margin: 0));
    logger.LogInformation("Pragmatic.Imaging native library OK");
}
catch (DllNotFoundException ex)
{
    logger.LogCritical(ex, "Pragmatic.Imaging native library not found — check deployment");
    throw;
}
```

---

## Building the native library yourself

The Rust source lives at `Pragmatic.Imaging/native/pragmatic-imaging/` — the crate that
produces the Imaging native library (`pragmatic_imaging_native`). It depends on the shared
`shared/native/pragmatic-core` crate for error/buffer plumbing.

Requires Rust **1.85+** (the crate uses edition 2024). Build it with:

```bash
cargo build --release \
  --manifest-path Pragmatic.Imaging/native/pragmatic-imaging/Cargo.toml
```

The output is at `Pragmatic.Imaging/native/pragmatic-imaging/target/release/`:

| Platform | Built file | Name the loader asks for |
|----------|------------|--------------------------|
| Windows | `pragmatic_imaging_native.dll` | `Pragmatic.Imaging.Native.dll` |
| Linux | `libpragmatic_imaging_native.so` | `libPragmatic.Imaging.Native.so` |
| macOS | `libpragmatic_imaging_native.dylib` | `libPragmatic.Imaging.Native.dylib` |

For a platform the package does not ship, copy the built file next to your app under the name in the
last column.

**Inside this repository**, the binaries in `Pragmatic.Imaging/runtimes/` are not copied by hand. Each
one carries the hash of the source it was built from, and the gate refuses one that does not match. The
one way to refresh them is `node scripts/refresh-native.mjs imaging` (add `--linux` for the linux-x64
build, which needs Docker). It runs the crate's tests, builds stamped, and places the files. The
pack step renames the Linux file to `libPragmatic.Imaging.Native.so`.

---

## Version compatibility

The native library and managed binding are versioned together. Mixing mismatched
versions (old managed + new native, or vice versa) can crash on the first FFI call.
If you build a native binary yourself, rebuild it from the same commit as the managed DLL.

---

## Troubleshooting deployment

See [troubleshooting.md](troubleshooting.md#deployment):

- "Unable to load DLL 'Pragmatic.Imaging.Native'"
- Alpine / musl failures
- Self-contained publish missing the native file
