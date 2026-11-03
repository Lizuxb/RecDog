import test from 'node:test';
import assert from 'node:assert/strict';
import { DocumentState } from '../src/document-state.mjs';

test('迟到的保存成功不能把保存期间的新输入标记为已保存', () => {
  const state = new DocumentState();
  state.load({ id: 'a', title: '访谈记录', content: { main: [] } });
  state.changed();
  const first = state.capture();
  state.changed();
  state.persisted(first);
  assert.equal(state.dirty, true);
  state.persisted(state.capture());
  assert.equal(state.dirty, false);
});

test('另一个文档的保存结果不能清除当前文档的编辑状态', () => {
  const state = new DocumentState();
  state.load({ id: 'a' });
  state.changed();
  const old = state.capture();
  state.load({ id: 'b' });
  state.changed();
  state.persisted(old);
  assert.equal(state.dirty, true);
});

test('保存失败没有提交保存代次，编辑仍然待保存', () => {
  const state = new DocumentState();
  state.load({ id: 'a' });
  state.changed();
  state.capture();
  assert.equal(state.dirty, true);
});
