import test from 'node:test';
import assert from 'node:assert/strict';
import { documentOwner, tableDimensions, canonicalJson } from '../src/workspace-state.mjs';

test('editing a project template while inside a person never saves it into that person', () => {
  const workspace = { documents: [{ id: 'template', kind: 'template' }], folders: [{ id: 'alice', documents: [{ id: 'note', kind: 'document' }] }] };
  assert.equal(documentOwner(workspace, 'template'), null);
  assert.equal(documentOwner(workspace, 'note'), 'alice');
  assert.throws(() => documentOwner(workspace, 'missing'));
});
test('table dimensions allow a user supplied 4 by 5 and reject invalid allocations', () => {
  assert.deepEqual(tableDimensions('4', '5'), [4, 5]);
  for (const [rows, cols] of [[0, 2], [2, 21], [1.5, 3], ['oops', 2]]) assert.throws(() => tableDimensions(rows, cols));
});
test('native JSON object key reordering preserves content while array order remains significant', () => {
  assert.equal(canonicalJson({ main: [{ value: '正文', size: 16 }], footer: [] }), canonicalJson({ footer: [], main: [{ size: 16, value: '正文' }] }));
  assert.notEqual(canonicalJson({ main: [1, 2] }), canonicalJson({ main: [2, 1] }));
});
