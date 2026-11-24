import { IntlMessageFormat } from 'intl-messageformat';
import { describe, expect, it } from 'vitest';

import ar from '../messages/ar.json';
import en from '../messages/en.json';
import fa from '../messages/fa.json';
import tr from '../messages/tr.json';
import { localeMetadata, locales } from '../src';

type Tree = { [key: string]: string | Tree };
const catalogs: Record<string, Tree> = { fa, en, tr, ar };

function flatten(tree: Tree, prefix = ''): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'string') out[path] = value;
    else Object.assign(out, flatten(value, path));
  }
  return out;
}

interface AstNode {
  type: number;
  value?: unknown;
  options?: Record<string, { value: AstNode[] }>;
  children?: AstNode[];
}

/** Argument names used by an ICU message (e.g. {name}, {count, plural, ...}), read from the parsed AST. */
function argumentsOf(message: string): string[] {
  const names = new Set<string>();
  const walk = (nodes: AstNode[]) => {
    for (const node of nodes) {
      // 0 literal, 7 pound: no argument. Everything else names an argument in `value`.
      if (node.type !== 0 && node.type !== 7 && typeof node.value === 'string') names.add(node.value);
      for (const option of Object.values(node.options ?? {})) walk(option.value);
      if (node.children) walk(node.children);
    }
  };
  walk(new IntlMessageFormat(message, 'en').getAst() as unknown as AstNode[]);
  return [...names].sort();
}

describe('message catalogs', () => {
  const reference = flatten(en);

  it('covers every supported locale', () => {
    expect(Object.keys(catalogs).sort()).toEqual([...locales].sort());
  });

  for (const locale of locales) {
    const flat = flatten(catalogs[locale]!);

    it(`${locale}: has exactly the same keys as en`, () => {
      expect(Object.keys(flat).sort()).toEqual(Object.keys(reference).sort());
    });

    it(`${locale}: every message is non-empty, valid ICU and uses the same arguments as en`, () => {
      for (const [key, message] of Object.entries(flat)) {
        expect(message.trim(), key).not.toBe('');
        expect(() => new IntlMessageFormat(message, localeMetadata[locale].intlLocale), `${locale}:${key}`).not.toThrow();
        expect(argumentsOf(message), `${locale}:${key}`).toEqual(argumentsOf(reference[key]!));
      }
    });
  }

  it('formats camelCase enum selects in every locale', () => {
    for (const locale of locales) {
      const flat = flatten(catalogs[locale]!);
      const status = new IntlMessageFormat(flat['people.statusLabel']!, locale).format({ status: 'inactive' });
      const fallback = new IntlMessageFormat(flat['people.statusLabel']!, locale).format({ status: 'graduated' });
      expect(status).not.toBe('inactive');
      expect(fallback).toBe('graduated');
    }
  });
});
