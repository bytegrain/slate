/** CSV (RFC 4180) and TSV (clipboard) export — port of Slate.Core `GridExport`. */
import { cellValue, col, displayText, type GridColumn } from './values';

type ValueOf<T> = (item: T, column: GridColumn<T>) => unknown;

/** Quotes a field when it contains a comma, quote, CR/LF or leading/trailing whitespace; quotes are doubled. */
export function csvField(field: string): string {
  const needs = /[",\r\n]/.test(field) || (field.length > 0 && (/\s/.test(field[0]!) || /\s/.test(field[field.length - 1]!)));
  return needs ? '"' + field.replaceAll('"', '""') + '"' : field;
}

/** Tabs and newlines inside a cell become spaces. */
export function tsvField(field: string): string {
  return field.replaceAll('\r\n', ' ').replace(/[\t\r\n]/g, ' ');
}

/** CSV with CRLF line breaks and no trailing newline. */
export function toCsv<T>(columns: readonly GridColumn<T>[], rows: Iterable<T>, includeHeader = true, valueOf?: ValueOf<T>): string {
  const lines: string[] = [];
  if (includeHeader) lines.push(columns.map((c) => csvField(col.title(c))).join(','));
  for (const row of rows) lines.push(columns.map((c) => csvField(displayText(c, valueOf ? valueOf(row, c) : cellValue(c, row)))).join(','));
  return lines.join('\r\n');
}

/** Tab-separated values with LF line breaks (for the clipboard). */
export function toTsv<T>(columns: readonly GridColumn<T>[], rows: Iterable<T>, includeHeader = false, valueOf?: ValueOf<T>): string {
  const lines: string[] = [];
  if (includeHeader) lines.push(columns.map((c) => tsvField(col.title(c))).join('\t'));
  for (const row of rows) lines.push(columns.map((c) => tsvField(displayText(c, valueOf ? valueOf(row, c) : cellValue(c, row)))).join('\t'));
  return lines.join('\n');
}
