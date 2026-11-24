import postcss from 'postcss';
import { describe, expect, it } from 'vitest';
import { classesInCss } from './ownClasses';
import { pluginStyles } from './pluginStyles';

async function stylesFor(classes: string, directives = '@tailwind components; @tailwind utilities;'): Promise<string> {
  return (await postcss([pluginStyles('markdown', [{ raw: classes, extension: 'html' }])]).process(directives, { from: undefined })).css;
}

describe('the styles of a plugin', () => {
  it('are the app\'s utilities under the plugin\'s prefix', async () => {
    const styles = await stylesFor('markdown-flex flex -markdown-mt-2 markdown-text-fg markdown-rounded-lg');

    expect(classesInCss(styles).sort()).toEqual(['-markdown-mt-2', 'markdown-flex', 'markdown-rounded-lg', 'markdown-text-fg']);
    expect(styles).toContain('var(--fg)');
    expect(styles).toContain('border-radius: 12px');
  });

  it('follow the app\'s dark theme, whose class has no prefix', async () => {
    const styles = await stylesFor('dark:markdown-hidden');

    expect(styles).toContain('[class~="dark"]');
    expect(styles).not.toContain('.markdown-dark');
  });

  it('bring neither preflight nor the theme\'s variables', async () => {
    const styles = await stylesFor('markdown-flex', '@tailwind base; @tailwind utilities;');

    expect(styles).not.toContain('--fg:');
    expect(styles).not.toContain('line-height');
  });
});
