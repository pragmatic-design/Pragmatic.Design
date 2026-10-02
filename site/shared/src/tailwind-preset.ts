import type { Config } from "tailwindcss";
import { colors, fonts, borderRadius } from "./tokens";

const preset: Partial<Config> = {
  theme: {
    extend: {
      colors: {
        primary: colors.primary,
        surface: colors.surface,
        text: colors.text,
        secondary: colors.secondary,
        outline: colors.outline,
      },
      fontFamily: {
        heading: fonts.heading,
        body: fonts.body,
        mono: fonts.mono,
      },
      borderRadius,
      backgroundImage: {
        "metal-gradient":
          "linear-gradient(135deg, #f2ca50 0%, #d4af37 50%, #f2ca50 100%)",
        "glass-gradient":
          "linear-gradient(135deg, rgba(53, 53, 52, 0.4) 0%, rgba(40, 40, 39, 0.2) 100%)",
      },
      boxShadow: {
        "glow-gold": "0 0 20px rgba(242, 202, 80, 0.15)",
        "glow-gold-lg": "0 0 40px rgba(242, 202, 80, 0.25)",
      },
      backdropBlur: {
        glass: "20px",
      },
    },
  },
};

export default preset;
