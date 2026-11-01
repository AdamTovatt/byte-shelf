/**
 * Single source of truth for the design system color palette.
 *
 * Twelve-step Radix-style scales per hue (neutral, brand, danger, success,
 * warning, info), each step assigned a fixed semantic role:
 *
 *   1  App background (canvas)            7  UI element border / focus ring
 *   2  Subtle background (panel)          8  Hovered UI element border
 *   3  UI element background (surface)    9  Solid backgrounds
 *   4  Hovered UI surface                 10 Hovered solid backgrounds
 *   5  Active / selected UI surface       11 Low-contrast text
 *   6  Subtle borders & separators        12 High-contrast text
 *
 * Each scale has a paired alpha scale (nested under `.a`) with the alpha baked
 * into the color, for translucent overlays where solids would tint the
 * underlying surface in the wrong direction.
 *
 * At build time, `tailwind/colors.js` walks this tree and emits:
 *   - One CSS variable per leaf (`--neutral-1`, `--brand-9`, `--neutral-a-3`, …)
 *     wired into `:root` / `.dark` / `[data-theme="…"]` blocks per active theme.
 *   - Bare Tailwind utilities (`bg-neutral-3`, `text-brand-12`, `bg-neutral-a-3`)
 *     that resolve through the variables and therefore swap with the active theme.
 *   - A functional alias layer (`bg-surface`, `text-strong`, …) — see
 *     `tailwind/colors.js` for the alias map.
 *
 * Hex values are Radix-shape scales; chroma is pulled at the extremes by design. Use the Radix
 * custom palette generator (https://www.radix-ui.com/colors/custom) when retuning a scale.
 */

export const colors = {
  /** Grey neutral. The light ramp is achromatic by design (R = G = B at every step) —
   *  it used to carry a few points of extra blue and read as a cool cast on the page;
   *  the dark ramp keeps its indigo tint, which reads as depth rather than as tinted
   *  white. Strict-order scale (step 1 lightest in
   *  light / darkest in dark). Functional aliases (canvas, panel, surface, …)
   *  pick different steps per theme — see `tailwind/colors.js` — so the active
   *  app palette can preserve the asymmetric "buttons brighter than page" feel
   *  the design uses without breaking the strict numbered order. */
  neutral: {
    1:  { light: '#F5F5F5', dark: '#171923' },
    2:  { light: '#ECECEC', dark: '#242634' },
    3:  { light: '#E3E3E3', dark: '#313344' },
    4:  { light: '#DADADA', dark: '#3E4055' },
    5:  { light: '#D1D1D1', dark: '#4B4D65' },
    6:  { light: '#C5C5C5', dark: '#5A5C77' },
    7:  { light: '#B5B5B5', dark: '#6E708A' },
    8:  { light: '#9C9C9C', dark: '#8B8DA8' },
    9:  { light: '#7D7D7D', dark: '#ACADC9' },
    10: { light: '#6B6B6B', dark: '#BEBFD5' },
    11: { light: '#4D4D4D', dark: '#D0D1E1' },
    12: { light: '#1A1A1A', dark: '#F4F5F9' },
    /** Alpha — black-with-alpha in light, white-with-alpha in dark. */
    a: {
      1:  { light: 'rgba(0, 0, 0, 0.012)',   dark: 'rgba(255, 255, 255, 0.012)' },
      2:  { light: 'rgba(0, 0, 0, 0.024)',   dark: 'rgba(255, 255, 255, 0.026)' },
      3:  { light: 'rgba(0, 0, 0, 0.047)',   dark: 'rgba(255, 255, 255, 0.045)' },
      4:  { light: 'rgba(0, 0, 0, 0.071)',   dark: 'rgba(255, 255, 255, 0.064)' },
      5:  { light: 'rgba(0, 0, 0, 0.106)',   dark: 'rgba(255, 255, 255, 0.088)' },
      6:  { light: 'rgba(0, 0, 0, 0.141)',   dark: 'rgba(255, 255, 255, 0.114)' },
      7:  { light: 'rgba(0, 0, 0, 0.220)',   dark: 'rgba(255, 255, 255, 0.144)' },
      8:  { light: 'rgba(0, 0, 0, 0.314)',   dark: 'rgba(255, 255, 255, 0.193)' },
      9:  { light: 'rgba(0, 0, 0, 0.456)',   dark: 'rgba(255, 255, 255, 0.286)' },
      10: { light: 'rgba(0, 0, 0, 0.510)',   dark: 'rgba(255, 255, 255, 0.378)' },
      11: { light: 'rgba(0, 0, 0, 0.636)',   dark: 'rgba(255, 255, 255, 0.601)' },
      12: { light: 'rgba(0, 0, 0, 0.898)',   dark: 'rgba(255, 255, 255, 0.927)' },
    },
  },
  /** Brand indigo. Step 9 is the canonical brand hex (#5551CD); matches across modes. */
  brand: {
    1:  { light: '#FBFAFE', dark: '#14131F' },
    2:  { light: '#F2F1FE', dark: '#1A1932' },
    3:  { light: '#D9DEFC', dark: '#262654' },
    4:  { light: '#C8CFFA', dark: '#2D2D5E' },
    5:  { light: '#B6BEF7', dark: '#34336B' },
    6:  { light: '#A1ABF5', dark: '#3D3D7B' },
    7:  { light: '#8B98F4', dark: '#4B4A8E' },
    8:  { light: '#7783F3', dark: '#5C5BA8' },
    9:  { light: '#5551CD', dark: '#5551CD' },
    10: { light: '#4944C0', dark: '#6D6AD4' },
    11: { light: '#3F38B5', dark: '#818CF8' },
    12: { light: '#1A1652', dark: '#ECEBFA' },
    a: {
      1:  { light: 'rgba(85, 81, 205, 0.016)',  dark: 'rgba(168, 165, 240, 0.020)' },
      2:  { light: 'rgba(85, 81, 205, 0.035)',  dark: 'rgba(168, 165, 240, 0.044)' },
      3:  { light: 'rgba(85, 81, 205, 0.078)',  dark: 'rgba(168, 165, 240, 0.106)' },
      4:  { light: 'rgba(85, 81, 205, 0.122)',  dark: 'rgba(168, 165, 240, 0.156)' },
      5:  { light: 'rgba(85, 81, 205, 0.180)',  dark: 'rgba(168, 165, 240, 0.220)' },
      6:  { light: 'rgba(85, 81, 205, 0.247)',  dark: 'rgba(168, 165, 240, 0.302)' },
      7:  { light: 'rgba(85, 81, 205, 0.345)',  dark: 'rgba(168, 165, 240, 0.420)' },
      8:  { light: 'rgba(85, 81, 205, 0.479)',  dark: 'rgba(168, 165, 240, 0.534)' },
      9:  { light: 'rgba(85, 81, 205, 1.000)',  dark: 'rgba(85, 81, 205, 1.000)' },
      10: { light: 'rgba(73, 68, 192, 1.000)',  dark: 'rgba(109, 106, 212, 1.000)' },
      11: { light: 'rgba(63, 56, 181, 1.000)',  dark: 'rgba(161, 158, 227, 1.000)' },
      12: { light: 'rgba(26, 22, 82, 1.000)',   dark: 'rgba(236, 235, 250, 1.000)' },
    },
  },
  /** Danger / red — destructive actions, errors. */
  danger: {
    1:  { light: '#FFFCFC', dark: '#190E0E' },
    2:  { light: '#FFF7F7', dark: '#220F11' },
    3:  { light: '#FFE5E5', dark: '#401016' },
    4:  { light: '#FFD5D5', dark: '#500F1C' },
    5:  { light: '#FCC3C4', dark: '#631823' },
    6:  { light: '#F6ADAE', dark: '#79232E' },
    7:  { light: '#EB949A', dark: '#973038' },
    8:  { light: '#E0747B', dark: '#C04046' },
    9:  { light: '#DC2626', dark: '#DC2626' },
    10: { light: '#C9202D', dark: '#EB4242' },
    11: { light: '#B41E2A', dark: '#FF8E8E' },
    12: { light: '#5F0E15', dark: '#FFD1D9' },
    a: {
      1:  { light: 'rgba(220, 38, 38, 0.012)', dark: 'rgba(255, 142, 142, 0.020)' },
      2:  { light: 'rgba(220, 38, 38, 0.031)', dark: 'rgba(255, 142, 142, 0.044)' },
      3:  { light: 'rgba(220, 38, 38, 0.086)', dark: 'rgba(255, 142, 142, 0.119)' },
      4:  { light: 'rgba(220, 38, 38, 0.165)', dark: 'rgba(255, 142, 142, 0.169)' },
      5:  { light: 'rgba(220, 38, 38, 0.224)', dark: 'rgba(255, 142, 142, 0.232)' },
      6:  { light: 'rgba(220, 38, 38, 0.322)', dark: 'rgba(255, 142, 142, 0.317)' },
      7:  { light: 'rgba(220, 38, 38, 0.420)', dark: 'rgba(255, 142, 142, 0.432)' },
      8:  { light: 'rgba(220, 38, 38, 0.541)', dark: 'rgba(255, 142, 142, 0.595)' },
      9:  { light: 'rgba(220, 38, 38, 1.000)', dark: 'rgba(220, 38, 38, 1.000)' },
      10: { light: 'rgba(201, 32, 45, 1.000)', dark: 'rgba(235, 66, 66, 1.000)' },
      11: { light: 'rgba(180, 30, 42, 1.000)', dark: 'rgba(255, 142, 142, 1.000)' },
      12: { light: 'rgba(95, 14, 21, 1.000)',  dark: 'rgba(255, 209, 217, 1.000)' },
    },
  },
  /** Success / green — confirmations, positive states. */
  success: {
    1:  { light: '#FBFEFB', dark: '#0C1610' },
    2:  { light: '#F2FCF5', dark: '#0E1F15' },
    3:  { light: '#DCFCE6', dark: '#0F2D1A' },
    4:  { light: '#C5F8D2', dark: '#0E3D22' },
    5:  { light: '#ADEFC0', dark: '#114E2C' },
    6:  { light: '#91E3AC', dark: '#14633A' },
    7:  { light: '#71D193', dark: '#167C49' },
    8:  { light: '#4ABE73', dark: '#169A5A' },
    9:  { light: '#16A34A', dark: '#4ADE80' },
    10: { light: '#129141', dark: '#6FE89A' },
    11: { light: '#0F7838', dark: '#88EFAA' },
    12: { light: '#06311A', dark: '#CFFCDA' },
    a: {
      1:  { light: 'rgba(22, 163, 74, 0.020)',  dark: 'rgba(74, 222, 128, 0.020)' },
      2:  { light: 'rgba(22, 163, 74, 0.051)',  dark: 'rgba(74, 222, 128, 0.048)' },
      3:  { light: 'rgba(22, 163, 74, 0.137)',  dark: 'rgba(74, 222, 128, 0.097)' },
      4:  { light: 'rgba(22, 163, 74, 0.224)',  dark: 'rgba(74, 222, 128, 0.169)' },
      5:  { light: 'rgba(22, 163, 74, 0.322)',  dark: 'rgba(74, 222, 128, 0.244)' },
      6:  { light: 'rgba(22, 163, 74, 0.434)',  dark: 'rgba(74, 222, 128, 0.341)' },
      7:  { light: 'rgba(22, 163, 74, 0.557)',  dark: 'rgba(74, 222, 128, 0.451)' },
      8:  { light: 'rgba(22, 163, 74, 0.706)',  dark: 'rgba(74, 222, 128, 0.585)' },
      9:  { light: 'rgba(22, 163, 74, 1.000)',  dark: 'rgba(74, 222, 128, 1.000)' },
      10: { light: 'rgba(18, 145, 65, 1.000)',  dark: 'rgba(111, 232, 154, 1.000)' },
      11: { light: 'rgba(15, 120, 56, 1.000)',  dark: 'rgba(136, 239, 170, 1.000)' },
      12: { light: 'rgba(6, 49, 26, 1.000)',    dark: 'rgba(207, 252, 218, 1.000)' },
    },
  },
  /** Warning / amber — caution states. */
  warning: {
    1:  { light: '#FEFDFB', dark: '#15110B' },
    2:  { light: '#FFFAF0', dark: '#1D170B' },
    3:  { light: '#FFEFCC', dark: '#2C200A' },
    4:  { light: '#FFE5B5', dark: '#382808' },
    5:  { light: '#FFD89A', dark: '#44310A' },
    6:  { light: '#FBCB7B', dark: '#523D10' },
    7:  { light: '#F1B85A', dark: '#634C18' },
    8:  { light: '#E5A032', dark: '#7E631F' },
    9:  { light: '#D97706', dark: '#FBBF24' },
    10: { light: '#BB6605', dark: '#FFD662' },
    11: { light: '#985404', dark: '#FFE187' },
    12: { light: '#432802', dark: '#FFEFC7' },
    a: {
      1:  { light: 'rgba(217, 119, 6, 0.015)',  dark: 'rgba(251, 191, 36, 0.025)' },
      2:  { light: 'rgba(217, 119, 6, 0.059)',  dark: 'rgba(251, 191, 36, 0.057)' },
      3:  { light: 'rgba(217, 119, 6, 0.200)',  dark: 'rgba(251, 191, 36, 0.122)' },
      4:  { light: 'rgba(217, 119, 6, 0.290)',  dark: 'rgba(251, 191, 36, 0.193)' },
      5:  { light: 'rgba(217, 119, 6, 0.400)',  dark: 'rgba(251, 191, 36, 0.260)' },
      6:  { light: 'rgba(217, 119, 6, 0.514)',  dark: 'rgba(251, 191, 36, 0.350)' },
      7:  { light: 'rgba(217, 119, 6, 0.647)',  dark: 'rgba(251, 191, 36, 0.456)' },
      8:  { light: 'rgba(217, 119, 6, 0.804)',  dark: 'rgba(251, 191, 36, 0.601)' },
      9:  { light: 'rgba(217, 119, 6, 1.000)',  dark: 'rgba(251, 191, 36, 1.000)' },
      10: { light: 'rgba(187, 102, 5, 1.000)',  dark: 'rgba(255, 214, 98, 1.000)' },
      11: { light: 'rgba(152, 84, 4, 1.000)',   dark: 'rgba(255, 225, 135, 1.000)' },
      12: { light: 'rgba(67, 40, 2, 1.000)',    dark: 'rgba(255, 239, 199, 1.000)' },
    },
  },
  /** Info / blue — informational highlights. */
  info: {
    1:  { light: '#FBFDFF', dark: '#0C1018' },
    2:  { light: '#F2F8FF', dark: '#0E1623' },
    3:  { light: '#DDECFF', dark: '#0F2240' },
    4:  { light: '#C5DDFF', dark: '#102E55' },
    5:  { light: '#ABCEFF', dark: '#133C6C' },
    6:  { light: '#8DBCFE', dark: '#174D85' },
    7:  { light: '#67A7FB', dark: '#1D5FA1' },
    8:  { light: '#4992F9', dark: '#2576BE' },
    9:  { light: '#3B82F6', dark: '#60A5FA' },
    10: { light: '#2470E6', dark: '#80B8FB' },
    11: { light: '#1B5BBE', dark: '#A9CDFC' },
    12: { light: '#0A2B61', dark: '#DDE9FE' },
    a: {
      1:  { light: 'rgba(59, 130, 246, 0.016)', dark: 'rgba(96, 165, 250, 0.020)' },
      2:  { light: 'rgba(59, 130, 246, 0.051)', dark: 'rgba(96, 165, 250, 0.044)' },
      3:  { light: 'rgba(59, 130, 246, 0.137)', dark: 'rgba(96, 165, 250, 0.114)' },
      4:  { light: 'rgba(59, 130, 246, 0.224)', dark: 'rgba(96, 165, 250, 0.180)' },
      5:  { light: 'rgba(59, 130, 246, 0.329)', dark: 'rgba(96, 165, 250, 0.253)' },
      6:  { light: 'rgba(59, 130, 246, 0.447)', dark: 'rgba(96, 165, 250, 0.345)' },
      7:  { light: 'rgba(59, 130, 246, 0.596)', dark: 'rgba(96, 165, 250, 0.464)' },
      8:  { light: 'rgba(59, 130, 246, 0.745)', dark: 'rgba(96, 165, 250, 0.604)' },
      9:  { light: 'rgba(59, 130, 246, 1.000)', dark: 'rgba(96, 165, 250, 1.000)' },
      10: { light: 'rgba(36, 112, 230, 1.000)', dark: 'rgba(128, 184, 251, 1.000)' },
      11: { light: 'rgba(27, 91, 190, 1.000)',  dark: 'rgba(169, 205, 252, 1.000)' },
      12: { light: 'rgba(10, 43, 97, 1.000)',   dark: 'rgba(221, 233, 254, 1.000)' },
    },
  },
  /** Foreground/text on top of brand step 9 (solid brand surfaces). Always white. */
  onBrand: { light: '#FFFFFF', dark: '#FFFFFF' },
  shadow: { light: 'rgba(0, 0, 0, 0.1)', dark: 'rgba(0, 0, 0, 0.3)' },
} as const;
