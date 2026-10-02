/**
 * Pragmatic Design — Design Tokens
 * Extracted from Stitch "PRAGMATIC DESIGN" project (gold/dark theme).
 */

export const colors = {
  primary: {
    DEFAULT: "#f2ca50",
    container: "#d4af37",
    fixed: "#ffe088",
    dim: "#b8962a",
  },
  surface: {
    DEFAULT: "#131313",
    container: "#201f1f",
    "container-high": "#2a2a2a",
    bright: "#393939",
  },
  text: {
    DEFAULT: "#e5e2e1",
    variant: "#d0c5af",
    muted: "#99907c",
  },
  secondary: {
    DEFAULT: "#c5c6ca",
    container: "#47494d",
  },
  outline: {
    DEFAULT: "#99907c",
    variant: "#4a4640",
  },
  code: {
    keyword: "#f2ca50",
    string: "#a8d4a0",
    comment: "#6a6a6a",
    type: "#e5c07b",
    function: "#61afef",
    number: "#d19a66",
  },
} as const;

export const fonts = {
  heading: ["Manrope", "system-ui", "sans-serif"],
  body: ["Inter", "system-ui", "sans-serif"],
  mono: ["JetBrains Mono", "Fira Code", "Consolas", "monospace"],
} as const;

export const borderRadius = {
  sm: "0.25rem",
  DEFAULT: "0.5rem",
  md: "0.75rem",
  lg: "1rem",
  xl: "1.5rem",
} as const;
