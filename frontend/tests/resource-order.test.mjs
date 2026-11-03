import { test } from 'node:test';
import assert from 'node:assert/strict';
import { resourceTime } from '../src/workspace-state.mjs';
test('saving an older document never moves it below its recording', () => {
  const document = { createdAt: '2026-11-03T09:00:00Z', updatedAt: '2026-11-03T11:00:00Z' };
  const recording = { createdAt: '2026-11-03T10:00:00Z' };
  assert.ok(resourceTime(document) < resourceTime(recording));
  assert.equal(resourceTime({ updatedAt: document.updatedAt }), Date.parse(document.updatedAt));
});
