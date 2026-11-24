import type { AcceptedPlugin } from 'postcss';
import tailwindcss, { type Config } from 'tailwindcss';
import { uiPreset } from '../src/ui/tailwind/preset.js';

/**
 * The PostCSS plugin that writes the utilities of the plugin `id` found in `content`: the app's scales
 * and colours under a class prefix of the plugin's own (`markdown-` in `hover:markdown-flex`), so its
 * utilities and the app's never meet. Preflight is off, and so are the theme's variables, both the app's
 * to set once.
 */
export function pluginStyles(id: string, content: Config['content']): AcceptedPlugin {
  return tailwindcss({
    presets: [{ ...uiPreset, plugins: [] }],
    prefix: `${id}-`,
    // An attribute selector, because Tailwind puts the prefix before a class in the selector too, and the
    // app marks the dark theme with `dark`.
    darkMode: ['variant', '&:is([class~="dark"] *)'],
    corePlugins: { preflight: false },
    content,
  });
}
