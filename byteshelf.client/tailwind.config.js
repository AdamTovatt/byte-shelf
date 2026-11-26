import { SM } from './src/lib/screens.ts';
import { uiPreset } from './src/ui/tailwind/preset.js';

/** @type {import('tailwindcss').Config} */
export default {
  // Colours, radius and type scales, and the shadows the components in src/ui name.
  presets: [uiPreset],
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      // Read by scripts too, which lay out a phone's screen as these classes do.
      screens: {
        sm: SM,
      },
      fontFamily: {
        sans: ['Inter Variable', 'Inter', 'system-ui', '-apple-system', 'sans-serif'],
      },
      backgroundImage: {
        // The progress bar's fill (src/ui ProgressBar), from the brand variables in variables.css.
        'brand-gradient': 'var(--brand-gradient)',
      },
      keyframes: {
        'sidebar-slide-in': {
          '0%': { transform: 'translateX(-100%)' },
          '100%': { transform: 'translateX(0)' },
        },
      },
      animation: {
        'sidebar-slide-in': 'sidebar-slide-in 250ms cubic-bezier(0.25, 0.1, 0.25, 1)',
      },
    },
  },
  plugins: [],
}
