import { tailwindColors, themeVariablesPlugin } from './colors.js';

/**
 * The Tailwind tokens the components in `src/ui` use: colours, radius and type scales, and the shadows
 * they name. Scales are kept whole, so the next component reaching for an unused rung gets this design
 * rather than stock Tailwind's. App-only tokens stay in `tailwind.config.js`.
 */

/** @type {import('tailwindcss').Config} */
export const uiPreset = {
  darkMode: 'class',
  // Only emit `hover:`/`group-hover:` styles under `@media (hover: hover)`, so hover affordances never apply
  // on touch devices: otherwise iOS Safari takes the first tap on a hover-styled row as "show the hover"
  // and swallows its click.
  future: {
    hoverOnlyWhenSupported: true,
  },
  theme: {
    extend: {
      colors: tailwindColors,

      // The radius scale. Every step is one rung larger than stock Tailwind's.
      borderRadius: {
        'sm': '4px',
        'DEFAULT': '8px',
        'md': '8px',
        'lg': '12px',
        'xl': '16px',
        '2xl': '20px',
        'full': '9999px',
      },

      // The type scale, each size carrying its own line height and letter spacing.
      fontSize: {
        'xs': ['0.75rem', { lineHeight: '1rem', letterSpacing: '0.01em' }],
        'sm': ['0.875rem', { lineHeight: '1.25rem', letterSpacing: '0.01em' }],
        'base': ['1rem', { lineHeight: '1.5rem', letterSpacing: '0.01em' }],
        'lg': ['1.125rem', { lineHeight: '1.75rem', letterSpacing: '0' }],
        'xl': ['1.25rem', { lineHeight: '1.875rem', letterSpacing: '-0.01em' }],
        '2xl': ['1.5rem', { lineHeight: '2rem', letterSpacing: '-0.01em' }],
        '3xl': ['1.875rem', { lineHeight: '2.25rem', letterSpacing: '-0.02em' }],
        '4xl': ['2.25rem', { lineHeight: '2.5rem', letterSpacing: '-0.02em' }],
        '5xl': ['3rem', { lineHeight: '3.5rem', letterSpacing: '-0.02em' }],
        '6xl': ['3.75rem', { lineHeight: '4rem', letterSpacing: '-0.03em' }],
        '7xl': ['4.5rem', { lineHeight: '5rem', letterSpacing: '-0.03em' }],
        '8xl': ['6rem', { lineHeight: '6.5rem', letterSpacing: '-0.04em' }],
      },

      boxShadow: {
        'subtle': '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
        'brand-lg': '0 2px 8px rgba(0, 0, 0, 0.08)',
        'focus-glow': '0 0 8px var(--focus-glow-color)',
        'login-card': '0 8px 32px rgba(0, 0, 0, 0.3)',
      },

      // Squircle surfaces re-express their box-shadow as a drop-shadow so the shadow follows the
      // superellipse rather than a rectangle. Mirrors the matching boxShadow tokens above.
      dropShadow: {
        'card-sm': '0 1px 2px rgba(0, 0, 0, 0.05)',
        'brand-lg': '0 2px 8px rgba(0, 0, 0, 0.08)',
        'focus-glow': '0 0 8px var(--focus-glow-color)',
      },
    },
  },
  plugins: [themeVariablesPlugin],
};

export default uiPreset;
