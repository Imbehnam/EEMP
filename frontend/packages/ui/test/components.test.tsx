// @vitest-environment jsdom
import { ThemeProvider } from '@emotion/react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { ConfirmDialog, DataTable, Pagination, SelectField, TextField, tokens } from '../src';

const wrap = (ui: ReactNode) => render(<ThemeProvider theme={tokens}>{ui}</ThemeProvider>);

describe('TextField', () => {
  it('links label, hint and error for assistive technology', () => {
    wrap(<TextField label="Email" hint="We never share it" error="Enter a valid email address." required />);
    const input = screen.getByLabelText(/Email/);
    expect(input).toBeRequired();
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('We never share it Enter a valid email address.');
  });
});

describe('SelectField', () => {
  it('renders a placeholder and options with their own language', () => {
    wrap(<SelectField label="Language" placeholder="Choose" options={[{ value: 'fa', label: 'فارسی', lang: 'fa' }]} defaultValue="" />);
    expect(screen.getByRole('option', { name: 'فارسی' })).toHaveAttribute('lang', 'fa');
    expect(screen.getByRole('option', { name: 'Choose' })).toHaveValue('');
  });
});

describe('DataTable', () => {
  const columns = [
    { key: 'name', header: 'Name', isRowHeader: true, cell: (r: { id: string; name: string }) => r.name },
    { key: 'id', header: 'Id', cell: (r: { id: string; name: string }) => r.id },
  ];
  it('uses a caption and row headers', () => {
    wrap(<DataTable caption="Branches" columns={columns} rows={[{ id: '1', name: 'North' }]} getRowKey={(r) => r.id} />);
    expect(screen.getByRole('table', { name: 'Branches' })).toBeInTheDocument();
    expect(screen.getByRole('rowheader', { name: 'North' })).toBeInTheDocument();
  });
  it('shows the empty state', () => {
    wrap(<DataTable caption="Branches" columns={columns} rows={[]} getRowKey={(r) => r.id} empty={<p>No branches yet.</p>} />);
    expect(screen.getByText('No branches yet.')).toBeInTheDocument();
  });
});

describe('Pagination', () => {
  it('disables previous on the first page and reports page changes', async () => {
    const onPageChange = vi.fn();
    wrap(
      <Pagination page={1} pageSize={20} totalCount={45} onPageChange={onPageChange}
        labels={{ nav: 'Pagination', previous: 'Previous', next: 'Next', summary: 'Showing 1–20 of 45' }} />,
    );
    expect(screen.getByRole('navigation', { name: 'Pagination' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Previous/ })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: /Next/ }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});

describe('ConfirmDialog', () => {
  it('renders an accessible dialog and calls confirm/cancel', async () => {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    wrap(
      <ConfirmDialog open title="Delete North?" description="This can’t be undone." confirmLabel="Delete" cancelLabel="Cancel"
        onConfirm={onConfirm} onCancel={onCancel} />,
    );
    const dialog = screen.getByRole('dialog', { hidden: true });
    expect(dialog).toHaveAccessibleName('Delete North?');
    await userEvent.click(screen.getByRole('button', { name: 'Delete', hidden: true }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    expect(onConfirm).toHaveBeenCalledOnce();
    expect(onCancel).toHaveBeenCalledOnce();
  });
});
