use image::{ImageBuffer, Rgba, ImageFormat};
use qrcode::QrCode;

use std::io::Cursor;

use pragmatic_core::set_last_error;

/// Generate a QR code as PNG bytes.
/// `size` is the module pixel size, `margin` is the quiet zone in modules.
/// Upper bound on the rendered QR side length, guarding against an out-of-memory
/// allocation: an 8192×8192 RGBA image is ~256 MB, and no legitimate QR needs more
/// (a max-version 177-module code fits at moduleSize 40 with margin).
const MAX_QR_DIMENSION: u32 = 8192;

pub fn generate_qr_png(text: &str, size: u32, margin: u32) -> Result<Vec<u8>, String> {
    let code = QrCode::new(text.as_bytes())
        .map_err(|e| format!("QR generation failed: {e}"))?;

    let module_count = code.width() as u32;
    // Checked arithmetic: a large module_size/margin must not wrap u32 and slip past the cap.
    let img_size = module_count
        .checked_mul(size)
        .and_then(|base| margin.checked_mul(2).and_then(|m| m.checked_mul(size)).map(|q| base.saturating_add(q)))
        .unwrap_or(u32::MAX);

    if img_size == 0 || img_size > MAX_QR_DIMENSION {
        return Err(format!(
            "QR image dimension ({img_size}px) exceeds limit ({MAX_QR_DIMENSION}px); reduce moduleSize or margin"
        ));
    }

    let mut img = ImageBuffer::from_pixel(img_size, img_size, Rgba([255u8, 255, 255, 255]));

    let colors = code.to_colors();
    for (i, &color) in colors.iter().enumerate() {
        let row = (i as u32) / module_count;
        let col = (i as u32) % module_count;

        if color == qrcode::Color::Dark {
            let px = margin * size + col * size;
            let py = margin * size + row * size;
            for dy in 0..size {
                for dx in 0..size {
                    if px + dx < img_size && py + dy < img_size {
                        img.put_pixel(px + dx, py + dy, Rgba([0u8, 0, 0, 255]));
                    }
                }
            }
        }
    }

    let mut buf = Cursor::new(Vec::new());
    img.write_to(&mut buf, ImageFormat::Png)
        .map_err(|e| format!("PNG encode failed: {e}"))?;

    Ok(buf.into_inner())
}

/// C ABI: generate QR code as PNG.
/// Allocates output buffer via Rust. Caller must free with `pragmatic_free_buffer`.
///
/// # Safety
/// `text` must point to `text_len` valid UTF-8 bytes. Output pointers must be non-null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_qr_generate(
    text: *const u8,
    text_len: usize,
    size: u32,
    margin: u32,
    out_png: *mut *mut u8,
    out_png_len: *mut usize,
) -> i32 {
    if text.is_null() || out_png.is_null() || out_png_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }

    let slice = unsafe { std::slice::from_raw_parts(text, text_len) };
    let text_str = match std::str::from_utf8(slice) {
        Ok(s) => s,
        Err(e) => {
            set_last_error(format!("Invalid UTF-8: {e}"));
            return -1;
        }
    };

    match generate_qr_png(text_str, size, margin) {
        Ok(png) => {
            let len = png.len();
            let boxed = png.into_boxed_slice();
            let ptr = Box::into_raw(boxed) as *mut u8;
            unsafe {
                *out_png = ptr;
                *out_png_len = len;
            }
            0
        }
        Err(e) => {
            set_last_error(e);
            -1
        }
    }
}
