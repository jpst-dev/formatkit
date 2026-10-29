import { expect, test } from 'vitest';
import { renderNotes } from '../src/lib/markdown';

test('renderNotes escapes raw HTML and renders markdown', () => {
  const html = renderNotes('note <script>x</script> **b**');
  expect(html).toContain('&lt;script&gt;');
  expect(html).not.toContain('<script>');
  expect(html).toContain('<strong>b</strong>');
});

test('renderNotes does not make javascript: links', () => {
  expect(renderNotes('[x](javascript:alert(1))')).not.toContain('href="javascript:');
});

test('renderNotes escapes a line that starts with a raw HTML block', () => {
  const html = renderNotes('<script>x</script> **b**');
  expect(html).toContain('&lt;script&gt;');
  expect(html).not.toContain('<script>');
});
