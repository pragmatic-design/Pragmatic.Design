use std::alloc::{GlobalAlloc, Layout, System};
use std::process::Command;
use std::sync::atomic::{AtomicUsize, Ordering};

use serde_json::{json, Value};

use crate::pdf::chunked::{render_with, CELL_BUDGET};
use crate::pdf::compile::get_fonts;
use crate::pdf::model::DocumentModel;

/// Counts the bytes alive in this test binary, and the most there were since `reset`. It sees the native
/// engine's memory, which is the part of a render's working set the .NET heap does not (#279).
struct Counting;
static LIVE: AtomicUsize = AtomicUsize::new(0);
static PEAK: AtomicUsize = AtomicUsize::new(0);

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let p = unsafe { System.alloc(layout) };
        if !p.is_null() {
            let live = LIVE.fetch_add(layout.size(), Ordering::Relaxed) + layout.size();
            PEAK.fetch_max(live, Ordering::Relaxed);
        }
        p
    }
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        unsafe { System.dealloc(ptr, layout) };
        LIVE.fetch_sub(layout.size(), Ordering::Relaxed);
    }
}

#[global_allocator]
static ALLOCATOR: Counting = Counting;

/// A 10,000-row render holds at most this much native memory at once. One compile of it held about 2 GB.
const PEAK_BOUND: usize = 250_000_000;

/// The benchmarks' report (`Workloads.ReportTemplate`): a title, a heading, a table of ten columns.
fn report(rows: usize) -> DocumentModel {
    let text = |content: String| json!({ "$type": "text", "content": content });
    let headers = ["#", "SKU", "Description", "Category", "Qty", "Unit price", "Discount", "VAT", "Net", "Shipped"];
    let lines: Vec<Value> = (0..rows).map(|r| {
        let cells = [
            r.to_string(), format!("SKU-{r:05}"), format!("Item number {r} description"), format!("Category {}", r % 7),
            (r % 13).to_string(), format!("{}.50", r % 100), "0.1".into(), "0.22".into(), format!("{}.25", r % 1000),
            "2026-10-16".into(),
        ].map(|c: String| json!({ "content": [text(c)] }));
        json!({ "cells": cells })
    }).collect();
    let columns = [10, 18, 30, 18, 10, 16, 14, 10, 16, 22].map(|w| json!({ "width": w }));
    let header = headers.map(|h| json!({ "content": [text(h.into())] }));
    serde_json::from_value(json!({
        "title": "Report",
        "pages": [{ "content": [
            { "$type": "heading", "content": "Report", "level": 2 },
            {
                "$type": "table",
                "columns": columns,
                "header": { "cells": header },
                "rows": lines,
                "repeatHeader": true
            }
        ]}]
    })).unwrap()
}

/// The peak of one render above what was alive before it, after a small one has paid for the fonts. The
/// model is built before: what it weighs is the caller's, as the parsed request is in the library.
fn peak_of(rows: usize, budget: usize) -> usize {
    render_with(&report(100), get_fonts(), budget).unwrap();
    let model = report(rows);
    let before = LIVE.load(Ordering::Relaxed);
    PEAK.store(before, Ordering::Relaxed);
    let started = std::time::Instant::now();
    let (pdf, chunks) = render_with(&model, get_fonts(), budget).unwrap();
    let peak = PEAK.load(Ordering::Relaxed) - before;
    println!("{chunks} chunk(s), {} B of PDF, {} ms", pdf.len(), started.elapsed().as_millis());
    assert!(pdf.starts_with(b"%PDF"));
    peak
}

/// Each measure runs in a process of its own: in this one, other tests allocate at the same time.
fn measure_alone(test: &str) -> String {
    let output = Command::new(std::env::current_exe().unwrap())
        .args(["--exact", test, "--ignored", "--nocapture", "--test-threads=1"])
        .output()
        .unwrap();
    let printed = format!("{}{}", String::from_utf8_lossy(&output.stdout), String::from_utf8_lossy(&output.stderr));
    assert!(output.status.success(), "{test} failed:\n{printed}");
    printed
}

#[test]
fn a_ten_thousand_row_report_renders_under_the_bound() {
    let printed = measure_alone("pdf::peak_memory_tests::measure_ten_thousand_rows_chunked");
    assert!(printed.contains("peak "), "{printed}");
}

/// The control: the same kind of report in one compile goes over the bound at a fifth of the rows, so
/// the bound tells the chunked render apart from one that is not.
#[test]
fn one_compile_of_a_long_report_goes_over_the_bound() {
    let printed = measure_alone("pdf::peak_memory_tests::measure_two_thousand_rows_in_one_compile");
    assert!(printed.contains("peak "), "{printed}");
}

#[test]
#[ignore = "run in a process of its own by a_ten_thousand_row_report_renders_under_the_bound"]
fn measure_ten_thousand_rows_chunked() {
    let peak = peak_of(10_000, CELL_BUDGET);
    println!("peak {peak} B, 10,000 rows in chunks");
    assert!(peak < PEAK_BOUND, "a 10,000-row render peaked at {peak} B, over the {PEAK_BOUND} B bound");
}

#[test]
#[ignore = "run in a process of its own by one_compile_of_a_long_report_goes_over_the_bound"]
fn measure_two_thousand_rows_in_one_compile() {
    let peak = peak_of(2_000, usize::MAX);
    println!("peak {peak} B, 2,000 rows in one compile");
    assert!(peak > PEAK_BOUND, "one compile of 2,000 rows peaked at {peak} B, under the {PEAK_BOUND} B bound");
}
