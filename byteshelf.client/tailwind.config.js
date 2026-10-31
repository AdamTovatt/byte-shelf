import { colors } from './src/lib/colors.ts';

/** Convert hex (#rrggbb) to "r, g, b" for rgba() usage in shadow strings. */
function hexToRgb(hex) {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : '0, 0, 0';
}

const brandRgb = hexToRgb(colors.brand.tint.light);

/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Geist', 'system-ui', '-apple-system', 'sans-serif'],
        heading: ['Space Grotesk', 'system-ui', 'sans-serif'],
      },
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
      colors,
      backgroundImage: {
        'brand-gradient': `linear-gradient(135deg, ${colors.brand.accent.light} 0%, ${colors.brand.tint.dark} 100%)`,
        'border-gradient': `linear-gradient(135deg, ${colors.brand.accent.light} 0%, ${colors.brand.tint.dark} 100%)`,
        'brand-shimmer': `linear-gradient(90deg, ${colors.brand.surface.light} 0%, #f0e6fa 50%, ${colors.brand.surface.light} 100%)`,
        'brand-shimmer-dark': `linear-gradient(90deg, ${colors.brand.surface.dark} 0%, #3d2e5c 50%, ${colors.brand.surface.dark} 100%)`,
      },
      borderRadius: {
        'sm': '4px',
        'DEFAULT': '8px',
        'md': '8px',
        'lg': '12px',
        'xl': '16px',
        '2xl': '20px',
        'full': '9999px',
      },
      spacing: {
        '18': '4.5rem',
        '88': '22rem',
        '92': '23rem',
        '100': '25rem',
        '104': '26rem',
        '108': '27rem',
        '112': '28rem',
        '116': '29rem',
        '120': '30rem',
      },
      boxShadow: {
        'brand': '0 1px 3px rgba(0, 0, 0, 0.06)',
        'brand-lg': '0 2px 8px rgba(0, 0, 0, 0.08)',
        'glow': `0 0 20px rgba(${brandRgb}, 0.15)`,
        'glow-lg': `0 0 40px rgba(${brandRgb}, 0.2)`,
        'focus-glow': '0 0 8px var(--focus-glow-color)',
        'subtle': '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
        'soft': '0 2px 8px 0 rgba(0, 0, 0, 0.06)',
        'card': '0 1px 3px rgba(0, 0, 0, 0.02), 0 1px 2px rgba(0, 0, 0, 0.03)',
        'card-hover': '0 4px 12px rgba(0, 0, 0, 0.03), 0 2px 4px rgba(0, 0, 0, 0.015)',
        'inset': 'inset 0 2px 4px rgba(0, 0, 0, 0.07), inset 0 1px 2px rgba(0, 0, 0, 0.05)',
        'inset-focus': 'inset 0 2px 4px rgba(0, 0, 0, 0.07), inset 0 1px 2px rgba(0, 0, 0, 0.05), 0 0 8px var(--focus-glow-color)',
        'panel-slide': '-4px 0 24px rgba(0, 0, 0, 0.15)',
        'login-card': '0 8px 32px rgba(0, 0, 0, 0.3)',
      },
      transitionDuration: {
        '400': '400ms',
        '600': '600ms',
      },
      transitionTimingFunction: {
        'smooth': 'cubic-bezier(0.4, 0, 0.2, 1)',
        'elegant': 'cubic-bezier(0.25, 0.1, 0.25, 1)',
      },
      keyframes: {
        'checkbox-bounce': {
          '0%': { transform: 'scale(1)' },
          '30%': { transform: 'scale(0.9)' },
          '70%': { transform: 'scale(1.1)' },
          '100%': { transform: 'scale(1)' },
        },
        // Shoots up fast from below the bottom edge, overshoots, then settles — and holds (fill forwards).
        'dog-hop': {
          '0%': { transform: 'translateY(120%)' },
          '33%': { transform: 'translateY(-10%)' },
          '67%': { transform: 'translateY(3%)' },
          '100%': { transform: 'translateY(0)' },
        },
        'slide-in-right': {
          '0%': { opacity: '0' },
          '100%': { opacity: '1' },
        },
        'sidebar-slide-in': {
          '0%': { transform: 'translateX(-100%)' },
          '100%': { transform: 'translateX(0)' },
        },
        'shimmer': {
          '0%': { backgroundPosition: '-200% 0' },
          '100%': { backgroundPosition: '200% 0' },
        },
      },
      animation: {
        'checkbox-bounce': 'checkbox-bounce 150ms ease-out',
        'dog-hop': 'dog-hop 0.5s ease-out forwards',
        'slide-in-right': 'slide-in-right 250ms cubic-bezier(0.25, 0.1, 0.25, 1)',
        'sidebar-slide-in': 'sidebar-slide-in 250ms cubic-bezier(0.25, 0.1, 0.25, 1)',
        'shimmer': 'shimmer 2s ease-in-out infinite',
      },
    },
  },
  plugins: [],
}
