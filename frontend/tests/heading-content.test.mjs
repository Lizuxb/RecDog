import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeHeadingContent } from '../src/heading-content.ts';

test('legacy level-only headings gain a shared native identity without changing source or formatting', () => {
  const source = {main:[{value:'旧',level:'first',size:26,bold:true},{value:'标题',level:'first',size:26},{value:'\n正文',size:16}]};
  const original = structuredClone(source);
  const fixed = normalizeHeadingContent(source);
  assert.ok(fixed.main[0].titleId);
  assert.equal(fixed.main[0].titleId, fixed.main[1].titleId);
  assert.equal(fixed.main[0].size, 26);
  assert.equal(fixed.main[0].bold, true);
  assert.equal(fixed.main[2].titleId, undefined);
  assert.deepEqual(source, original);
});

test('line breaks separate legacy headings even when their levels match', () => {
  const fixed = normalizeHeadingContent({main:[{value:'\n标题甲\n\n标题乙',level:'second',font:'宋体',size:20}]});
  assert.equal(fixed.main.map(e => e.value).join(''), '\n标题甲\n\n标题乙');
  const titles = fixed.main.filter(e => e.titleId);
  assert.equal(titles.length, 2);
  assert.notEqual(titles[0].titleId, titles[1].titleId);
});

test('preserve native title groups and do not infer headings from bold or large text', () => {
  const source = {main:[{type:'title',value:'',level:'first',titleId:'native',valueList:[{value:'原生标题',size:24}]},{value:'加粗正文',bold:true,size:24}]};
  assert.deepEqual(normalizeHeadingContent(source), source);
});

test('legacy headings in table cells are normalized independently and normalization is stable', () => {
  const fixed = normalizeHeadingContent({main:[{type:'table',value:'',trList:[{tdList:[{value:[{value:'单元格标题',level:'second'}]},{value:[{value:'另一标题',level:'second'}]}]}]}]});
  const cells = fixed.main[0].trList[0].tdList;
  assert.ok(cells[0].value[0].titleId);
  assert.notEqual(cells[0].value[0].titleId,cells[1].value[0].titleId);
  assert.deepEqual(normalizeHeadingContent(fixed),fixed);
});
