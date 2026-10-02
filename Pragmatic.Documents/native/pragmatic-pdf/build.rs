// The source stamp: scripts/refresh-native.mjs and the pdf-native workflow pass the hash of the source
// they build from, and the gate reads it back out of the committed binaries (scripts/native-stamp.mjs).
// A build without it — a bare `cargo build` — is marked "unstamped", and the gate refuses that binary
// instead of shipping it.
fn main() {
    println!("cargo:rerun-if-env-changed=PRAGMATIC_PDF_SOURCE_HASH");
    let hash = std::env::var("PRAGMATIC_PDF_SOURCE_HASH").unwrap_or_else(|_| "unstamped".to_string());
    println!("cargo:rustc-env=PRAGMATIC_PDF_SOURCE_STAMP=PRAGMATIC_PDF_SOURCE_HASH:{hash}");
}
