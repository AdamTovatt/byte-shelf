import plugin from 'tailwindcss/plugin';
import { colors } from '../lib/colors.ts';

function parseHex(hex) {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  if (!m) return null;
  return [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)];
}

/** "r g b" (space-separated) — for `rgb(var(--x) / <alpha-value>)`. */
function hexToRgbChannels(hex) {
  const rgb = parseHex(hex);
  return rgb ? rgb.join(' ') : null;
}

function isLightDarkHexLeaf(value) {
  return (
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    typeof value.light === 'string' &&
    typeof value.dark === 'string' &&
    hexToRgbChannels(value.light) !== null &&
    hexToRgbChannels(value.dark) !== null
  );
}

function isRgbaString(value) {
  return typeof value === 'string' && /^rgba?\(/i.test(value);
}

/** Alpha leaf: { light: 'rgba(…)', dark: 'rgba(…)' } — alpha is baked into the value. */
function isAlphaLeaf(value) {
  return (
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    isRgbaString(value.light) &&
    isRgbaString(value.dark)
  );
}

/**
 * Fixed leaf: { fixed: '#hex' } — one value for every theme. For palettes that deliberately do not
 * follow light/dark at all (a fixed-scheme page pinning `data-theme="light"`), so they don't have to
 * write the same hex twice and invite someone to "fix" a dark value that was never a dark value.
 * Emits into both theme blocks, and exposes only the bare utility: there is no per-mode variant to
 * address, so no `-light` / `-dark` sibling is generated.
 */
function isFixedHexLeaf(value) {
  return (
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    typeof value.fixed === 'string' &&
    hexToRgbChannels(value.fixed) !== null
  );
}

/**
 * Fixed ALPHA leaf: { fixed: 'rgba(…)' } — one translucent value for every theme. Same intent as
 * {@link isFixedHexLeaf}: a palette that deliberately does not follow light/dark at all. Separate because
 * the alpha is baked into the value, so it emits the variable directly rather than as channels + an
 * `<alpha-value>` slot. Used by `colors.paperInk`, whose whole point is being correct on the always-white
 * docx page in all four themes (#1616).
 */
function isFixedAlphaLeaf(value) {
  return (
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    isRgbaString(value.fixed)
  );
}

/**
 * Walks a palette and produces:
 *   - `tree`: same shape with a `DEFAULT` sibling on every leaf.
 *     - Hex leaves: `rgb(var(--<path>) / <alpha-value>)` so opacity modifiers work.
 *     - Alpha leaves: `var(--<path>)` (alpha is already baked into the value).
 *   - `lightMap` / `darkMap`: flat `{ '--<path>': '<value>' }` for the theme blocks.
 *     - Hex: space-separated channels (e.g. "236 236 238").
 *     - Alpha: complete rgba literal.
 *
 * Paths are lowercased so CSS variable identifiers stay kebab-case even when
 * source keys are camelCase (`onBrand` → `--onbrand`).
 *
 * Used on `colors` to derive the Tailwind utility tree and the theme blocks.
 */
function buildVarColors(palette, path = []) {
  if (isLightDarkHexLeaf(palette)) {
    const varName = `--${path.join('-').toLowerCase()}`;
    return {
      tree: {
        ...palette,
        DEFAULT: `rgb(var(${varName}) / <alpha-value>)`,
      },
      lightMap: { [varName]: hexToRgbChannels(palette.light) },
      darkMap: { [varName]: hexToRgbChannels(palette.dark) },
    };
  }
  if (isAlphaLeaf(palette)) {
    const varName = `--${path.join('-').toLowerCase()}`;
    return {
      tree: {
        ...palette,
        DEFAULT: `var(${varName})`,
      },
      lightMap: { [varName]: palette.light },
      darkMap: { [varName]: palette.dark },
    };
  }
  if (isFixedAlphaLeaf(palette)) {
    const varName = `--${path.join('-').toLowerCase()}`;
    return {
      tree: { DEFAULT: `var(${varName})` },
      lightMap: { [varName]: palette.fixed },
      darkMap: { [varName]: palette.fixed },
    };
  }
  if (isFixedHexLeaf(palette)) {
    const varName = `--${path.join('-').toLowerCase()}`;
    const channels = hexToRgbChannels(palette.fixed);
    return {
      tree: { DEFAULT: `rgb(var(${varName}) / <alpha-value>)` },
      lightMap: { [varName]: channels },
      darkMap: { [varName]: channels },
    };
  }
  if (palette && typeof palette === 'object' && !Array.isArray(palette)) {
    const tree = {};
    let lightMap = {};
    let darkMap = {};
    for (const key of Object.keys(palette)) {
      const child = buildVarColors(palette[key], [...path, String(key)]);
      tree[key] = child.tree;
      lightMap = { ...lightMap, ...child.lightMap };
      darkMap = { ...darkMap, ...child.darkMap };
    }
    return { tree, lightMap, darkMap };
  }
  return { tree: palette, lightMap: {}, darkMap: {} };
}

/**
 * Functional aliases — single names that map to a numbered step per theme. These
 * are the daily-driver utilities (`bg-surface`, `text-muted`, `border-border`,
 * etc.).
 *
 * Aliases can map to a different step in light vs dark, which lets us preserve
 * the existing app's asymmetric "buttons brighter than page in light, brighter
 * than page in dark (by different amounts)" feel without breaking the strict
 * numbered order of the underlying scale. A `'step-N'` string means "same step
 * in both themes"; `{ light: 'step-N', dark: 'step-M' }` picks per-theme.
 *
 * Naming convention: name reads cleanly with at least one common Tailwind
 * prefix without doubling words. E.g. `surface` works as `bg-surface`;
 * `muted` works as `text-muted`. Avoid `text-muted` as a color name (it would
 * produce `text-text-muted`).
 *
 * For roles where no single short name exists, use the numbered token directly
 * (e.g. `bg-neutral-5` for a surface a step past `surface-hover`). Not for TEXT:
 * inks are `fg`, `muted` and `neutral-9`, and nothing between them.
 */
const functionalAliases = {
  // Surfaces — asymmetric per theme: in light, panel (step 1) is brighter than
  // canvas (step 2); in dark, panel (step 2) is brighter than canvas (step 1).
  // Keeps buttons reading as brighter than the page while preserving the strict
  // numbered order of the underlying scale.
  'canvas':         { light: 'neutral-2', dark: 'neutral-1' },
  'panel':          { light: 'neutral-1', dark: 'neutral-2' },
  'surface':        'neutral-3',
  'surface-hover':  'neutral-4',
  'surface-active': 'neutral-5',
  'solid':          'neutral-9',
  'solid-hover':    'neutral-10',

  // Border points at surface (step 3) for subtle hairlines that match card surfaces.
  'border':         'neutral-3',

  // Text roles
  'muted':          'neutral-11',
  'fg':             'neutral-12',

  // Brand functional aliases. NOTE: no bare `brand` alias — it would collide
  // with variables.css's decorative `--brand` (full rgb() literal for raw-CSS
  // gradients). Use `brand-9` directly for the saturated solid brand surface.
  'brand-surface':       'brand-3',
  'brand-surface-hover': 'brand-4',
  'brand-border':        'brand-7',
  // brand-tint is per-theme so dark-mode focus rings / glows stay bright (a
  // higher-step value), while light mode picks the saturated mid tint at step 8.
  'brand-tint':          { light: 'brand-8', dark: 'brand-11' },
  'brand-text':          'brand-12',
  'on-brand':            'onbrand',

  // Mac Finder-style row selection. A selected row takes a translucent wash of this accent blue
  // (`bg-selected/15`, hover `/20`) behind its normal text — not a solid fill — so it reads in both
  // themes: blue-9 in light; a deeper blue-8 in dark, where step 9 is a pale blue whose wash would
  // barely register.
  'selected':            { light: 'info-9', dark: 'info-8' },
};

/**
 * Resolve one side of a target to its CSS value: a bare step/alias name becomes
 * `var(--<name>)` (so it follows the active theme), while a `{ raw }` object
 * emits literal space-separated RGB channels directly — for the handful of
 * values with no underlying scale step (e.g. pure white, or a pre-mixed tint).
 */
function targetToValue(target) {
  if (target && typeof target === 'object' && typeof target.raw === 'string') {
    return target.raw;
  }
  return `var(--${target})`;
}

/**
 * Emit one `--<name>: <value>` pair per entry. A bare string target means "same
 * in both modes"; `{ light, dark }` picks per-theme. Each side is either an
 * alias/step name or a `{ raw }` literal (see `targetToValue`).
 */
function aliasesToVars(aliases) {
  const lightMap = {};
  const darkMap = {};
  for (const [name, target] of Object.entries(aliases)) {
    const light = typeof target === 'string' ? target : target.light;
    const dark = typeof target === 'string' ? target : target.dark;
    lightMap[`--${name}`] = targetToValue(light);
    darkMap[`--${name}`] = targetToValue(dark);
  }
  return { lightMap, darkMap };
}

/**
 * Semantic tokens — named indirections that map to a functional alias per theme.
 * Targets are functional alias names (e.g. `panel`), not numbered steps, so the
 * tokens automatically pick up whatever per-theme step the alias resolves to.
 *
 *   - btn-main: the one main action's fill — the text colour, so it inverts with the theme.
 *   - field: the surface every input and the secondary button share (see below).
 */
const semanticTokens = {
  // The MAIN ACTION (#2455): one per form, card or modal, filled with the text colour (`fg`: near-black
  // in light, the near-white dark-mode ink in dark) and lettered in `panel`, so the pair inverts with
  // the theme. Every other button is the `field`-white secondary below; there is no third look.
  'btn-main':            'fg',
  'on-btn-main':         'panel',
  // The one hover for anything pressable that rests on the plain surface or the field: what a row or
  // control lights up with under the pointer, rather than each taking a step of its own. `surface` in
  // both themes, the sidebar's hover too: light used to take `surface-hover`, a step darker, so a menu
  // row and a sidebar row lit in two different greys.
  'row-hover':           'surface',
  // The FIELD: one surface for every control a person types into or picks from, and for the
  // secondary button — the standard white (`panel`) with a grey hairline, squircled. An input and
  // a secondary button are the same object to a reader (a control defined by its edge rather than by
  // a fill), so they share one name rather than two that happen to agree.
  //
  // `panel` in both themes, not literal #fff (#2455): a pure-white field made the panel
  // cards around it read as grey, though the cards had not changed. On a panel card a field is level
  // with its ground and its hairline is what outlines it. That hairline is the card's own `border`,
  // with no field token of its own: fields, secondary buttons, IconButtons and FilterChip all draw
  // one border grey, the card's, in both themes (#2455). A field is never grey at rest; under the
  // pointer a pressable one (a secondary button, a filter chip) takes `row-hover`, the menu's hover,
  // rather than a step of its own — a `canvas` step made a hovered control on the bare page vanish
  // into the page behind it.
  'field':                'panel',
};

const main = buildVarColors(colors);
const aliases = aliasesToVars(functionalAliases);
const semantic = aliasesToVars(semanticTokens);

/**
 * Tailwind colors map. Every numbered scale step, alpha step, functional
 * alias, and semantic token is addressable as a bare utility.
 */
export const tailwindColors = {
  ...main.tree,
  ...Object.fromEntries(
    Object.keys(functionalAliases).map((name) => [name, `rgb(var(--${name}) / <alpha-value>)`]),
  ),
  ...Object.fromEntries(
    Object.keys(semanticTokens).map((name) => [name, `rgb(var(--${name}) / <alpha-value>)`]),
  ),
};

export const themeVariablesPlugin = plugin(({ addBase }) => {
  const lightVars = { ...main.lightMap, ...aliases.lightMap, ...semantic.lightMap };
  const darkVars  = { ...main.darkMap,  ...aliases.darkMap,  ...semantic.darkMap };
  addBase({
    // The document foreground floor, so an element that names no text colour inherits a theme token
    // rather than the browser's black. `[data-theme]` is in the selector because `color` is
    // inherited rather than re-resolved, so a themed subtree must re-declare it to pick up its own
    // `--fg`.
    'body, [data-theme]': { color: 'rgb(var(--fg))' },
    // Defaults: light-theme values.
    ':root': lightVars,
    // `.dark` and `[data-theme="dark"]` both flip into dark mode. `.dark` stays
    // alive so Tailwind's `dark:` variant (used by the modal
    // backdrop blur, the force-dark sidebar, and asymmetric one-offs) still
    // resolves; `[data-theme="dark"]` is the switch on <html> set by ThemeContext.
    '.dark, [data-theme="dark"]': darkVars,
    // `[data-theme="light"]` re-pins variables to light values for nested light
    // scopes (e.g. a light card inside an otherwise-dark subtree).
    '[data-theme="light"]': lightVars,
  });
});
