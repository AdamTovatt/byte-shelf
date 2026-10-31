/**
 * Single source of truth for the design system color palette.
 * Used by both tailwind.config.js and runtime code.
 *
 * NOTE: Some values are duplicated in variables.css and index.css (autofill overrides)
 * and must be updated manually when changing colors here.
 */
export const colors = {
  elevation: {
    1: { dark: '#171923', light: '#ECECEE' },
    2: { dark: '#242634', light: '#F5F5F7' },
    3: { dark: '#313344', light: '#E3E3E5' },
    4: { dark: '#3E4055', light: '#DADADE' },
    5: { dark: '#4B4D65', light: '#D1D1D5' },
  },
  brand: {
    primary: { light: '#9777c7', dark: '#7b5aab' },
    accent: { light: '#a485d0', dark: '#6a4d96' },
    surface: { light: '#e8ddf4', dark: '#2e2544' },
    /** Bright purple for subtle interactive hints (hover borders, focus rings, glows) */
    tint: { light: '#b495d9', dark: '#c4a8e2' },
    /** Contrast text for use on brand primary/accent backgrounds */
    onPrimary: { light: '#FFFFFF', dark: '#FFFFFF' },
  },
  /** Text colors — based on elevation from the opposite mode with doubled step size (1 = strongest, 5 = faintest) */
  text: {
    1: { light: '#1A1A1A', dark: '#F4F5F9' },
    2: { light: '#333333', dark: '#E2E3ED' },
    3: { light: '#4D4D4D', dark: '#D0D1E1' },
    4: { light: '#6B6B6B', dark: '#BEBFD5' },
    5: { light: '#868686', dark: '#ACADC9' },
  },
  info: { light: '#3b82f6', dark: '#60a5fa' },
  success: { light: '#16a34a', dark: '#4ade80' },
  warning: { light: '#d97706', dark: '#fbbf24' },
  danger: { light: '#dc2626', dark: '#f87171' },
  border: { dark: '#313344', light: '#E3E3E5' },
  shadow: { dark: 'rgba(0, 0, 0, 0.3)', light: 'rgba(0, 0, 0, 0.1)' },
} as const;

/**
 * Vivid party-popper palette for celebratory confetti bursts — gold, purple, pink, and blue drawn
 * from the 🎉 emoji rather than the (calmer) brand scale. Kept as a standalone export so it stays
 * out of the Tailwind color spread; it's only ever handed to the confetti renderer at runtime.
 */
export const confettiColors: string[] = [
  '#F4C430', // gold
  '#FFE14D', // bright yellow
  '#B15CE0', // purple
  '#F0486B', // pink-red
  '#3E7BE8', // blue
  '#FF7EB6', // pink
];
