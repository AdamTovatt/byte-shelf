import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MarkdownView } from './MarkdownView';

/** Counts how often a text is worked out. */
const parsed = vi.hoisted(() => vi.fn());

vi.mock('remark-gfm', async (original) => {
  const { default: gfm } = await original<typeof import('remark-gfm')>();
  return {
    default(this: unknown, ...options: unknown[]) {
      parsed();
      return Reflect.apply(gfm, this, options) as unknown;
    },
  };
});

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** More quotes inside one another than can be worked out, and few enough to try it fast. */
const TOO_DEEP = 2_500;

describe('a Markdown file', () => {
  let root: Root;
  let container: HTMLDivElement;

  beforeEach(() => {
    parsed.mockClear();
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  function show(text: string, cutAt: number | null = null): Element {
    act(() => root.render(<MarkdownView text={text} cutAt={cutAt} />));
    return container.querySelector('article')!;
  }

  function press(label: string): void {
    act(() => container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!.click());
  }

  it('reads as what it describes', () => {
    const document = show('# Title\n\nSome **bold** and ~~struck~~ text.\n\n- [x] done\n- [ ] not');

    expect(document.querySelector('h1')!.textContent).toBe('Title');
    expect(document.querySelector('strong')!.textContent).toBe('bold');
    expect(document.querySelector('del')!.textContent).toBe('struck');
    expect([...document.querySelectorAll('input')].map(({ checked, disabled }) => [checked, disabled])).toEqual([[true, true], [false, true]]);
  });

  it('shows GitHub\'s tables', () => {
    const document = show('| Name | Size |\n| --- | ---: |\n| a.txt | 1 |');

    expect([...document.querySelectorAll('th')].map((each) => each.textContent)).toEqual(['Name', 'Size']);
    expect([...document.querySelectorAll('td')].map((each) => each.textContent)).toEqual(['a.txt', '1']);
  });

  it('shows none of the HTML in it', () => {
    const document = show('before <b onclick="alert(1)">marked</b>\n\n<script>alert(1)</script>\n\n<img src="https://elsewhere.test/x.png">\n\n<iframe src="https://elsewhere.test"></iframe>\n\nafter');

    expect(document.querySelector('b, script, img, iframe')).toBeNull();
    expect(document.textContent).not.toContain('<');
    expect(document.textContent).toContain('before marked');
    expect(document.textContent).toContain('after');
  });

  it('loads no image: its text stands in', () => {
    const document = show('![a map](https://elsewhere.test/map.png "north up") ![a chart](chart.png) ![drawn][ref] ![](untitled.png)\n\n[ref]: https://elsewhere.test/drawn.svg');

    expect(document.querySelector('img, picture, svg')).toBeNull();
    expect(document.innerHTML).not.toContain('.png');
    expect(document.innerHTML).not.toContain('.svg');
    expect(document.textContent!.trim().replace(/\s+/g, ' ')).toBe('a map a chart drawn');
    expect(document.querySelector('span')!.title).toBe('north up');
  });

  it('opens web links in a new tab that knows nothing of this one', () => {
    const document = show('[site](https://elsewhere.test/a?b=c "there") and HTTP://elsewhere.test and www.elsewhere.test');

    const links = [...document.querySelectorAll('a')];
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['https://elsewhere.test/a?b=c', 'HTTP://elsewhere.test', 'http://www.elsewhere.test']);
    expect(links.map((link) => [link.target, link.rel])).toEqual(links.map(() => ['_blank', 'noopener noreferrer']));
    expect(links[0].title).toBe('there');
  });

  it.each(['other.md', '/shelves/private', '#part', '../up.md', '//elsewhere.test/a', '?preview=1', 'mailto:someone@elsewhere.test', 'javascript:alert(1)', 'data:text/html,x', 'file:///etc/passwd'])('shows a link to %s as plain text', (address) => {
    const document = show(`before [the text](${address}) after`);

    expect(document.querySelector('a')).toBeNull();
    expect(document.textContent!.trim()).toBe('before the text after');
  });

  it('shows footnotes as text, with their heading', () => {
    const document = show('Said[^1].\n\n[^1]: By someone.');

    expect(document.querySelector('a')).toBeNull();
    expect(document.querySelector('section h2')!.outerHTML).toBe('<h2>Footnotes</h2>');
    expect(document.textContent).toContain('By someone.');
  });

  it('has a wide table scroll by itself', () => {
    const document = show('| Name |\n| --- |\n| a.txt |');

    expect(document.querySelector('table')!.parentElement!.className).toBe('markdown-overflow-x-auto');
  });

  it('switches to its source and back, to the same document', () => {
    const document = show('# Title');
    expect(container.querySelector('pre')).toBeNull();

    press('Source');
    expect(container.querySelector('pre')!.textContent).toBe('# Title');
    expect(document.closest('[hidden]')).not.toBeNull();

    press('Preview');
    expect(container.querySelector('pre')).toBeNull();
    expect(container.querySelector('article')).toBe(document);
    expect(document.closest('[hidden]')).toBeNull();
  });

  it('says that only its start is shown, of one that was cut, either way', () => {
    show('# Title', 1024 * 1024);
    expect(container.textContent).toContain('Only the first 1 MiB is shown.');

    press('Source');
    expect(container.querySelector('pre')!.parentElement!.textContent).toBe('# Title');
    expect(container.textContent!.match(/Only the first 1 MiB is shown\./g)).toHaveLength(2);
  });

  it('shows the start of a long one as it reads, and more of it as its source', () => {
    const text = `a${'å'.repeat(100 * 1024)}`;
    const document = show(text);

    // Two bytes each after the first, so the cut would go through one.
    expect(document.textContent).toBe(`a${'å'.repeat(64 * 1024 - 1)}`);
    expect(container.textContent).toContain('Only the first 128 KiB is shown.');

    press('Source');
    expect(container.querySelector('pre')!.textContent).toBe(text);
  });

  it('shows its source all the same when it cannot be shown as it reads, and the next one again', { timeout: 30_000 }, () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const nested = '>'.repeat(TOO_DEEP);
    show(nested);
    expect(container.textContent).toContain('This document could not be shown as it reads.');

    press('Source');
    expect(container.querySelector('pre')!.textContent).toBe(nested);

    expect(show('# Title').textContent).toBe('Title');
    vi.restoreAllMocks();
  });

  it('works out what it reads as only once for one text, however often it is drawn', () => {
    const document = show('# Title');
    const { firstElementChild } = document;

    show('# Title');

    expect(parsed).toHaveBeenCalledTimes(1);
    expect(document.firstElementChild).toBe(firstElementChild);
  });
});
