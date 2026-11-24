import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import postcss from 'postcss';
import tailwindcss from 'tailwindcss';
import ts from 'typescript';
import type { Plugin, Rollup } from 'vite';
import appStyles from '../tailwind.config.js';

/** Fails the build of a plugin on a utility without its class prefix, in its source or in its stylesheet. */
export function ownClassesOnly(): Plugin {
  const inSource = new Map<string, string>();

  return {
    name: 'byteshelf:own-classes-only',
    enforce: 'pre',
    transform(code, id) {
      const file = id.split('?')[0];
      if (/\.tsx?$/.test(file) && !file.includes('/node_modules/')) {
        for (const each of classesInSource(file, code)) inSource.set(each, file);
      }
      return undefined;
    },
    generateBundle: {
      // After Vite has written the stylesheet.
      order: 'post',
      async handler(_, bundle) {
        const stylesheets = Object.values(bundle).filter((output): output is Rollup.OutputAsset => output.type === 'asset' && output.fileName.endsWith('.css'));
        const inCss = stylesheets.flatMap((output) => classesInCss(String(output.source)));
        const refused = await unprefixedUtilities([...inSource.keys(), ...inCss]);
        if (refused.length > 0) {
          this.error(`Utilities without the plugin's class prefix: ${refused.map((each) => `${each} (${inSource.get(each) ?? 'its stylesheet'})`).join(', ')}.`);
        }
      },
    },
  };
}

// Utilities too: Tailwind leaves out those the app itself does not use.
const typeRecipes = readFileSync(resolve(import.meta.dirname, '../src/ui/typography.css'), 'utf8');

/**
 * Those of `classes` that are utilities of the app's, which have no prefix. The app's stylesheet has many
 * of them, so a plugin using one is styled by it or not as the app happens to, and a plugin shipping one
 * changes the app: a plugin's utility loaded after the app's `hidden` un-hides what the app hid.
 */
export async function unprefixedUtilities(classes: Iterable<string>): Promise<string[]> {
  const candidates = [...new Set(classes)];
  // `block` keeps Tailwind from warning that it found nothing, which is how it should be.
  const content = [{ raw: ['block', ...candidates].join(' '), extension: 'html' }];
  const { root } = await postcss([tailwindcss({ ...appStyles, content })]).process(`@tailwind base; @tailwind components; @tailwind utilities; ${typeRecipes}`, { from: undefined });

  const generated = new Set(classesInCss(root.toString()));
  return candidates.filter((candidate) => generated.has(candidate));
}

/** The classes in the selectors of `css`, unescaped. */
export function classesInCss(css: string): string[] {
  const found: string[] = [];
  postcss.parse(css).walkRules((rule) => {
    for (const [, name] of rule.selector.matchAll(/\.((?:\\[0-9a-f]{1,6}\s?|\\.|[\w-]|[\u0080-￿])+)/gi)) {
      found.push(name.replace(/\\(?:([0-9a-f]{1,6})\s?|(.))/gi, (_, code: string | undefined, character: string) => (code ? String.fromCodePoint(parseInt(code, 16)) : character)));
    }
  });
  return found;
}

/**
 * The classes named in a module's JSX (`className`, and any other property ending in it) and in its calls
 * to `cn`. In an expression those are the strings it can come to, not those it compares or looks up by.
 */
export function classesInSource(fileName: string, source: string): string[] {
  const found: string[] = [];

  function collect(node: ts.Node): void {
    if (ts.isTypeNode(node)) return;

    if (ts.isStringLiteralLike(node) || ts.isTemplateHead(node) || ts.isTemplateMiddleOrTemplateTail(node)) {
      found.push(...node.text.split(/\s+/).filter(Boolean));
    } else if (ts.isConditionalExpression(node)) {
      visit(node.condition);
      collect(node.whenTrue);
      collect(node.whenFalse);
    } else if (ts.isBinaryExpression(node)) {
      const operator = node.operatorToken.kind;
      const eitherSide = operator === ts.SyntaxKind.BarBarToken || operator === ts.SyntaxKind.QuestionQuestionToken || operator === ts.SyntaxKind.PlusToken;
      (eitherSide ? collect : visit)(node.left);
      (eitherSide || operator === ts.SyntaxKind.AmpersandAmpersandToken ? collect : visit)(node.right);
    } else if (ts.isElementAccessExpression(node)) {
      const variants = node.expression;
      if (ts.isObjectLiteralExpression(variants)) variants.properties.forEach((each) => (ts.isPropertyAssignment(each) ? collect(each.initializer) : visit(each)));
      else collect(variants);
      visit(node.argumentExpression);
    } else if (ts.isCallExpression(node) && !isCn(node)) {
      // `['a', 'b'].join(' ')` comes to what it is called on.
      collect(node.expression);
      node.arguments.forEach(visit);
    } else {
      // The keys of `cn({ key: on })`; a quoted one is a string above.
      if (ts.isPropertyAssignment(node) && ts.isIdentifier(node.name)) found.push(node.name.text);
      ts.forEachChild(node, collect);
    }
  }

  function visit(node: ts.Node): void {
    if (ts.isJsxAttribute(node) && /classname$/i.test(node.name.getText()) && node.initializer) collect(node.initializer);
    else if (ts.isCallExpression(node) && isCn(node)) node.arguments.forEach(collect);
    else ts.forEachChild(node, visit);
  }

  visit(ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX));
  return found;
}

function isCn(call: ts.CallExpression): boolean {
  return call.expression.getText() === 'cn';
}
