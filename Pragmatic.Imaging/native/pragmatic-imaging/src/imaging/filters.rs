use image::{ImageBuffer, Rgba};
use image::imageops;

use pragmatic_core::set_last_error;

fn rgba_from_raw(data: &[u8], w: u32, h: u32) -> Result<ImageBuffer<Rgba<u8>, Vec<u8>>, String> {
    ImageBuffer::from_raw(w, h, data.to_vec())
        .ok_or_else(|| "Invalid RGBA buffer dimensions".to_string())
}

unsafe fn alloc_output(pixels: Vec<u8>, out: *mut *mut u8, out_len: *mut usize) {
    let len = pixels.len();
    let boxed = pixels.into_boxed_slice();
    let ptr = Box::into_raw(boxed) as *mut u8;
    unsafe {
        *out = ptr;
        *out_len = len;
    }
}

/// Convert to grayscale (preserving RGBA layout — alpha untouched).
pub fn grayscale(data: &[u8], w: u32, h: u32) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, w, h)?;
    let mut result = img.into_raw();
    // Process RGBA pixels in chunks of 4, preserving alpha
    for pixel in result.chunks_exact_mut(4) {
        // ITU-R BT.601 luminance
        let gray = (0.299 * pixel[0] as f32
                  + 0.587 * pixel[1] as f32
                  + 0.114 * pixel[2] as f32) as u8;
        pixel[0] = gray;
        pixel[1] = gray;
        pixel[2] = gray;
        // pixel[3] (alpha) is preserved
    }
    Ok(result)
}

/// Gaussian blur with the given sigma.
pub fn blur(data: &[u8], w: u32, h: u32, sigma: f32) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, w, h)?;
    let blurred = imageops::blur(&img, sigma);
    Ok(blurred.into_raw())
}

/// Unsharpen mask: sharpen with given sigma and threshold.
pub fn sharpen(data: &[u8], w: u32, h: u32, sigma: f32, threshold: i32) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, w, h)?;
    let sharpened = imageops::unsharpen(&img, sigma, threshold);
    Ok(sharpened.into_raw())
}

/// Adjust brightness. `value` is added to each channel (-255 to 255).
pub fn brightness(data: &[u8], w: u32, h: u32, value: i32) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, w, h)?;
    let adjusted = imageops::brighten(&img, value);
    Ok(adjusted.into_raw())
}

/// Adjust contrast. `value` is the contrast adjustment (-100.0 to 100.0).
pub fn contrast(data: &[u8], w: u32, h: u32, value: f32) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, w, h)?;
    let adjusted = imageops::contrast(&img, value);
    Ok(adjusted.into_raw())
}

// --- C ABI exports ---

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_grayscale(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match grayscale(slice, w, h) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_blur(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    sigma: f32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match blur(slice, w, h, sigma) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_sharpen(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    sigma: f32,
    threshold: i32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match sharpen(slice, w, h, sigma, threshold) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_brightness(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    value: i32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match brightness(slice, w, h, value) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_contrast(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    value: f32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match contrast(slice, w, h, value) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}
