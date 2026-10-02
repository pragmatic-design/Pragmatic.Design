use image::{ImageBuffer, Rgba};
use image::imageops::{self, FilterType};

use pragmatic_core::set_last_error;

/// Resize filter codes matching C# `ResizeFilter` enum.
#[repr(i32)]
#[derive(Debug, Clone, Copy)]
#[allow(dead_code)]
pub enum ResizeFilterCode {
    Nearest = 0,
    Triangle = 1,
    CatmullRom = 2,
    Gaussian = 3,
    Lanczos3 = 4,
}

impl ResizeFilterCode {
    fn to_filter_type(self) -> FilterType {
        match self {
            ResizeFilterCode::Nearest => FilterType::Nearest,
            ResizeFilterCode::Triangle => FilterType::Triangle,
            ResizeFilterCode::CatmullRom => FilterType::CatmullRom,
            ResizeFilterCode::Gaussian => FilterType::Gaussian,
            ResizeFilterCode::Lanczos3 => FilterType::Lanczos3,
        }
    }
}

impl TryFrom<i32> for ResizeFilterCode {
    type Error = String;
    fn try_from(value: i32) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(ResizeFilterCode::Nearest),
            1 => Ok(ResizeFilterCode::Triangle),
            2 => Ok(ResizeFilterCode::CatmullRom),
            3 => Ok(ResizeFilterCode::Gaussian),
            4 => Ok(ResizeFilterCode::Lanczos3),
            _ => Err(format!("Invalid resize filter: {value}. Valid: 0-4.")),
        }
    }
}

fn rgba_from_raw(data: &[u8], w: u32, h: u32) -> Result<ImageBuffer<Rgba<u8>, Vec<u8>>, String> {
    ImageBuffer::from_raw(w, h, data.to_vec())
        .ok_or_else(|| "Invalid RGBA buffer dimensions".to_string())
}

fn output_pixels(img: ImageBuffer<Rgba<u8>, Vec<u8>>) -> (Vec<u8>, u32, u32) {
    let w = img.width();
    let h = img.height();
    (img.into_raw(), w, h)
}

/// Resize RGBA8 pixels.
pub fn resize(
    data: &[u8], src_w: u32, src_h: u32,
    dst_w: u32, dst_h: u32, filter: ResizeFilterCode,
) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, src_w, src_h)?;
    let resized = imageops::resize(&img, dst_w, dst_h, filter.to_filter_type());
    Ok(resized.into_raw())
}

/// Crop RGBA8 pixels.
pub fn crop(
    data: &[u8], w: u32, h: u32,
    x: u32, y: u32, crop_w: u32, crop_h: u32,
) -> Result<Vec<u8>, String> {
    if crop_w == 0 || crop_h == 0 {
        return Err("Crop dimensions must be greater than zero".to_string());
    }
    if x.saturating_add(crop_w) > w || y.saturating_add(crop_h) > h {
        return Err(format!(
            "Crop region ({x},{y},{crop_w},{crop_h}) exceeds image bounds ({w}x{h})"
        ));
    }
    let mut img = rgba_from_raw(data, w, h)?;
    let cropped = imageops::crop(&mut img, x, y, crop_w, crop_h).to_image();
    Ok(cropped.into_raw())
}

/// Rotate RGBA8 pixels. `degrees` must be 90, 180, or 270.
pub fn rotate(data: &[u8], w: u32, h: u32, degrees: i32) -> Result<(Vec<u8>, u32, u32), String> {
    let img = rgba_from_raw(data, w, h)?;
    let rotated = match degrees {
        90 => output_pixels(imageops::rotate90(&img)),
        180 => output_pixels(imageops::rotate180(&img)),
        270 => output_pixels(imageops::rotate270(&img)),
        _ => return Err(format!("Unsupported rotation: {degrees}°. Use 90, 180, or 270.")),
    };
    Ok(rotated)
}

/// Flip RGBA8 pixels. `horizontal`: true=horizontal, false=vertical.
pub fn flip(data: &[u8], w: u32, h: u32, horizontal: bool) -> Result<Vec<u8>, String> {
    let img = rgba_from_raw(data, w, h)?;
    let flipped = if horizontal {
        imageops::flip_horizontal(&img)
    } else {
        imageops::flip_vertical(&img)
    };
    Ok(flipped.into_raw())
}

// --- C ABI exports ---

unsafe fn alloc_output(pixels: Vec<u8>, out: *mut *mut u8, out_len: *mut usize) {
    let len = pixels.len();
    let boxed = pixels.into_boxed_slice();
    let ptr = Box::into_raw(boxed) as *mut u8;
    unsafe {
        *out = ptr;
        *out_len = len;
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_resize(
    rgba: *const u8, len: usize,
    src_w: u32, src_h: u32,
    dst_w: u32, dst_h: u32,
    filter: i32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    let f = match ResizeFilterCode::try_from(filter) {
        Ok(f) => f,
        Err(e) => { set_last_error(e); return -1; }
    };
    match resize(slice, src_w, src_h, dst_w, dst_h, f) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_crop(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    x: u32, y: u32, crop_w: u32, crop_h: u32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match crop(slice, w, h, x, y, crop_w, crop_h) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_rotate(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    degrees: i32,
    out: *mut *mut u8, out_len: *mut usize,
    out_w: *mut u32, out_h: *mut u32,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null()
        || out_w.is_null() || out_h.is_null()
    {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match rotate(slice, w, h, degrees) {
        Ok((pixels, new_w, new_h)) => {
            unsafe {
                alloc_output(pixels, out, out_len);
                *out_w = new_w;
                *out_h = new_h;
            }
            0
        }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_flip(
    rgba: *const u8, len: usize,
    w: u32, h: u32,
    horizontal: i32,
    out: *mut *mut u8, out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    match flip(slice, w, h, horizontal != 0) {
        Ok(pixels) => { unsafe { alloc_output(pixels, out, out_len); } 0 }
        Err(e) => { set_last_error(e); -1 }
    }
}
