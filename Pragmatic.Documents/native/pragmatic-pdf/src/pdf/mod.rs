pub mod model;
pub mod style;
pub mod typst_gen;
pub mod table_gen;
pub mod compile;
pub mod operations;
pub mod segment;
pub mod chunked;
pub mod join;

#[cfg(test)]
mod compile_tests;
#[cfg(test)]
mod operations_tests;
#[cfg(test)]
mod style_tests;
#[cfg(test)]
mod chunked_tests;
#[cfg(test)]
mod peak_memory_tests;
