/** Applies a session's cumulative partial hypotheses at the cursor captured at recording start. */
export class LiveTranscriptSession {
  /** @param {(status: {state: string, message: string}) => void} [onStatus] */
  constructor(sessionId, anchor, editor, onStatus = () => {}) {
    if (!sessionId || !anchor || !Number.isInteger(anchor.startIndex) || !Number.isInteger(anchor.endIndex)) {
      throw new Error('实时转写需要有效的文档光标位置。');
    }
    this.sessionId = sessionId;
    this.anchor = { ...anchor };
    this.editor = editor;
    this.onStatus = onStatus;
    this.partial = '';
    this.active = true;
    this.prefix = '';
    this.fullText = '';
    this.completedSentences = new Set();
  }

  retarget(anchor) {
    if (!this.active) return;
    this.prefix = this.fullText;
    this.partial = '';
    this.anchor = { ...anchor };
    this.partialEnd = anchor.endIndex;
  }

  accept(event) {
    if (!this.active || event.sessionId !== this.sessionId) return false;
    if (event.error) { this.onStatus({ state: 'failed', message: event.error }); this.active = false; return true; }
    else if (event.state) this.onStatus({ state: event.state, message: event.message || '' });
    if (event.sentenceId != null && this.completedSentences.has(event.sentenceId)) return false;
    if (typeof event.text === 'string' && (event.text.length > 0 || event.isFinal || this.partial)) {
      this.fullText = event.text;
      // Retain what was visible before a click; do not duplicate the sentence's cumulative prefix.
      const text = this.prefix ? event.text.slice(Math.min(this.prefix.length, event.text.length)) : event.text;
      const replaceRange = this.partial
        ? { ...this.anchor, endIndex: this.partialEnd ?? this.anchor.startIndex + this.partial.length }
        : this.anchor;
      const actual = this.editor.replaceLiveTranscript(replaceRange, this.partial, text);
      this.anchor = { ...this.anchor, endIndex: this.anchor.startIndex };
      this.partial = text;
      this.partialEnd = actual && Number.isInteger(actual.endIndex) ? actual.endIndex : this.anchor.startIndex + text.length;
      if (actual && Number.isInteger(actual.startIndex)) this.anchor = { ...actual, startIndex: actual.startIndex, endIndex: actual.startIndex };
      if (event.isFinal) {
        this.anchor = { ...this.anchor, startIndex: this.partialEnd, endIndex: this.partialEnd };
        this.partial = '';
        this.prefix = ''; this.fullText = '';
        if (event.sentenceId != null) this.completedSentences.add(event.sentenceId);
      }
    }
    if (event.state === 'stopped' || event.state === 'failed') this.active = false;
    return true;
  }

  finish() { this.active = false; }
}
