import test from 'node:test';
import assert from 'node:assert/strict';
import { LiveTranscriptSession } from '../src/live-transcript.mjs';

function session() {
  const replacements = []; const statuses = [];
  const target = new LiveTranscriptSession('session-1', { startIndex: 20, endIndex: 22 }, { replaceLiveTranscript: (...args) => replacements.push(args) }, status => statuses.push(status));
  return { target, replacements, statuses };
}

test('partial hypotheses replace at the captured range, final freezes and next utterance appends', () => {
  const { target, replacements } = session();
  assert.equal(target.accept({ sessionId: 'session-1', text: '今天' }), true);
  assert.equal(target.accept({ sessionId: 'session-1', text: '今天开会' }), true);
  assert.equal(target.accept({ sessionId: 'session-1', text: '今天开会。', isFinal: true }), true);
  assert.equal(target.accept({ sessionId: 'session-1', text: '下午' }), true);
  assert.deepEqual(replacements.map(([range, previous, next]) => [range.startIndex, range.endIndex, previous, next]), [
    [20, 22, '', '今天'], [20, 22, '今天', '今天开会'], [20, 24, '今天开会', '今天开会。'], [25, 25, '', '下午']
  ]);
});

test('events from another session and late events after finish cannot insert', () => {
  const { target, replacements } = session();
  assert.equal(target.accept({ sessionId: 'old-session', text: '错误项目' }), false);
  target.finish();
  assert.equal(target.accept({ sessionId: 'session-1', text: '迟到文字' }), false);
  assert.equal(replacements.length, 0);
});

test('partial text and failure status are reported without requiring confirmation', () => {
  const { target, replacements, statuses } = session();
  target.accept({ sessionId: 'session-1', state: 'listening', message: '实时识别中' });
  target.accept({ sessionId: 'session-1', error: '麦克风流结束' });
  assert.deepEqual(statuses, [{ state: 'listening', message: '实时识别中' }, { state: 'failed', message: '麦克风流结束' }]);
  assert.equal(replacements.length, 0);
});

test('moving cursor retains old partial and routes only new suffix to the new anchor', () => {
  const { target, replacements } = session();
  target.accept({ sessionId: 'session-1', text: '今天开会', sentenceId: 1 });
  target.retarget({ startIndex: 100, endIndex: 100 });
  target.accept({ sessionId: 'session-1', text: '今天开会讨论方案', sentenceId: 1 });
  target.accept({ sessionId: 'session-1', text: '今天开会讨论方案。', sentenceId: 1, isFinal: true });
  assert.deepEqual(replacements.at(-1).slice(1), ['讨论方案', '讨论方案。']);
  assert.equal(replacements.at(-1)[0].startIndex, 100);
});

test('error must retain already inserted draft, final must not be inserted twice', () => {
  const { target, replacements } = session();
  target.accept({ sessionId: 'session-1', text: '一二三', sentenceId: 1 });
  target.accept({ sessionId: 'session-1', error: '网络断开', text: '', isFinal: true });
  assert.equal(replacements.length, 1);
  const second = session();
  const event = { sessionId: 'session-1', sentenceId: 1, text: '你好。', isFinal: true };
  second.target.accept(event); second.target.accept(event);
  assert.equal(second.replacements.length, 1);
});
