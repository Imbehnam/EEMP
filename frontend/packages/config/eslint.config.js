// Shared ESLint flat config for the EEMP frontend workspace.
import js from '@eslint/js';
import nextPlugin from '@next/eslint-plugin-next';
import i18next from 'eslint-plugin-i18next';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

/** @param {{ nextAppDirs?: string[] }} [options] */
export function createConfig(options = {}) {
  const nextAppDirs = options.nextAppDirs ?? [];
  return tseslint.config(
    {
      ignores: [
        '**/node_modules/**',
        '**/.next/**',
        '**/coverage/**',
        '**/playwright-report/**',
        '**/test-results/**',
        '**/next-env.d.ts',
      ],
    },
    js.configs.recommended,
    ...tseslint.configs.recommended,
    {
      languageOptions: {
        globals: { ...globals.browser, ...globals.node },
      },
      plugins: { 'react-hooks': reactHooks, 'jsx-a11y': jsxA11y },
      rules: {
        'react-hooks/rules-of-hooks': 'error',
        'react-hooks/exhaustive-deps': 'warn',
        ...jsxA11y.flatConfigs.recommended.rules,
        '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
        '@typescript-eslint/consistent-type-imports': 'error',
        'no-console': ['error', { allow: ['warn', 'error'] }],
      },
    },
    // No hard-coded user-facing text in JSX: every string must come from the i18n catalogs.
    {
      files: ['apps/*/src/**/*.tsx', 'packages/ui/src/**/*.tsx', 'packages/app/src/**/*.tsx'],
      ignores: ['**/*.test.tsx'],
      plugins: { i18next },
      rules: {
        'i18next/no-literal-string': ['error', { mode: 'jsx-text-only' }],
      },
    },
    ...nextAppDirs.map((dir) => ({
      files: [`${dir}/**/*.{ts,tsx}`],
      plugins: { '@next/next': nextPlugin },
      settings: { next: { rootDir: dir } },
      rules: {
        ...nextPlugin.configs.recommended.rules,
        ...nextPlugin.configs['core-web-vitals'].rules,
      },
    })),
    {
      files: ['**/*.test.{ts,tsx}', '**/e2e/**/*.ts', '**/*.config.{ts,mjs,js}'],
      rules: { '@typescript-eslint/no-non-null-assertion': 'off' },
    },
  );
}

export default createConfig();
