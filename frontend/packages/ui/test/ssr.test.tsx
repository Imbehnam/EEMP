import { CacheProvider, ThemeProvider } from '@emotion/react';
import type { ReactNode } from 'react';
import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import {
  Alert, Badge, Button, Card, Checkbox, ConfirmDialog, DataTable, DescriptionList, PageHeader, SearchBox,
  SelectField, Skeleton, StatCard, StatePanel, TextAreaField, TextField, ToastProvider, createEmotionCache, tokens,
} from '../src';

/** Every component must serialize through both Emotion caches (the RTL one runs stylis-plugin-rtl). */
const components: Record<string, ReactNode> = {
  alert: <Alert tone="danger" title="t">x</Alert>,
  badge: <Badge tone="success">x</Badge>,
  button: <Button loading>x</Button>,
  card: <Card>x</Card>,
  checkbox: <Checkbox label="x" />,
  dialog: <ConfirmDialog open={false} title="t" confirmLabel="ok" cancelLabel="no" onConfirm={() => {}} onCancel={() => {}} />,
  table: <DataTable caption="c" columns={[{ key: 'a', header: 'A', cell: (r: { a: string }) => r.a }]} rows={[]} getRowKey={(r) => r.a} loading />,
  header: <PageHeader title="x" description="d" actions={<Button>a</Button>} />,
  search: <SearchBox label="s" onSearch={() => {}} />,
  select: <SelectField label="x" options={[{ value: 'a', label: 'A' }]} error="e" />,
  skeleton: <Skeleton />,
  stat: <StatCard label="x" value="1" />,
  state: <StatePanel title="x" tone="warning" />,
  text: <TextField label="x" error="e" hint="h" required />,
  textarea: <TextAreaField label="x" />,
  dl: <DescriptionList><dt>a</dt><dd>b</dd></DescriptionList>,
  toast: <ToastProvider closeLabel="c">x</ToastProvider>,
};

describe.each(['ltr', 'rtl'] as const)('server rendering (%s)', (dir) => {
  it.each(Object.keys(components))('%s', (name) => {
    const html = renderToString(
      <CacheProvider value={createEmotionCache(dir)}>
        <ThemeProvider theme={tokens}>{components[name]}</ThemeProvider>
      </CacheProvider>,
    );
    expect(html.length).toBeGreaterThan(0);
  });
});
