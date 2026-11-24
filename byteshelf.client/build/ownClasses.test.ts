import { describe, expect, it } from 'vitest';
import { classesInCss, classesInSource, unprefixedUtilities } from './ownClasses';

describe('a plugin\'s classes that are the app\'s', () => {
  it('are the app\'s utilities, whatever they hold', async () => {
    const utilities = ['flex', 'hover:hidden', 'md:w-[10px]', '2xl:flex', 'grid-cols-[1fr,2fr]', 'text-fg', 'container'];
    const appOnly = ['text-body-md', 'bg-brand-gradient', 'animate-sidebar-slide-in'];
    const own = ['markdown-flex', '2xl:markdown-flex', 'textLayer', 'flexible'];

    expect(await unprefixedUtilities([...own, ...utilities, ...appOnly])).toEqual([...utilities, ...appOnly]);
  });

  it('are found in a stylesheet\'s selectors, unescaped', () => {
    const css = '.a:hover > .md\\:b, .w-\\[10px\\] { color: red } @media print { .c.\\32xl\\:d, .e-\\[1fr\\2c 2fr\\] .f {} }';

    expect(classesInCss(css)).toEqual(['a', 'md:b', 'w-[10px]', 'c', '2xl:d', 'e-[1fr,2fr]', 'f']);
  });

  it('are found in class attributes and calls to cn, and nowhere else in the source', () => {
    const source = `
      const table = 'table';
      export const A = () => <Card className="one two" title="hidden" iconClassName={on ? 'three' : \`four \${five} six\`}>
        <p className={cn('seven', { eight: on }, ['nine'])} />{cn('ten')}
      </Card>;`;

    expect(classesInSource('a.tsx', source).sort()).toEqual(['eight', 'four', 'nine', 'one', 'seven', 'six', 'ten', 'three', 'two']);
  });

  it('are what an expression can come to, not what it compares or looks up by', () => {
    const source = `
      export const A = () => <>
        <p className={status === 'hidden' ? styles['table'] : t('fixed')} />
        <p className={['sticky'].includes(kind) ? 'one' : (kind as 'flex')} />
        <p className={given || 'two' + suffix} />
        <p className={{ grid: 'three', inline: 'four' }[variant]} />
        <p className={['five', t(cn('six'))].join(' ')} />
        <p className={cn({ seven: 'static' === status, eight: status === 'block' }, isOpen && 'eleven')} />
        <p className={({ isActive }) => (isActive ? 'nine' : 'ten')} />
      </>;`;

    expect(classesInSource('a.tsx', source).sort()).toEqual(['eight', 'eleven', 'five', 'four', 'nine', 'one', 'seven', 'six', 'ten', 'three', 'two']);
  });
});
