import { request, type WorkspaceRecording } from './bridge';

// The post-recording transcription UI is paused; keep its implementation for a future release.
export const offlineTranscriptionEnabled = false;

interface Transcript { recordingId: string; model: string; text: string; editedText: string | null; revision: number; createdAt: string }
interface Job { id: string; folderId: string; recordingId: string; recordingName: string; state: 'loading' | 'transcribing' | 'completed' | 'failed' | 'canceled'; message: string; completedSeconds: number; totalSeconds: number; error?: string }
interface Settings { pythonPath: string; modelPath: string; ready: boolean; message: string }
const escape = (value: string) => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
/** Owns transient view state only. Resource IDs and persistence belong to the host. */
export class TranscriptionPanel {
  private root: HTMLElement;
  private outline: HTMLElement;
  private target: { folderId: string; id: string; name: string } | null = null;
  private transcript: Transcript | null = null;
  private job: Job | null = null;
  private generation = 0;
  private timer?: ReturnType<typeof setTimeout>;
  private draft = '';
  private pending = false;
  private notice = '';
  constructor(private insert: (text: string) => void, enabled = true) {
    const panel = document.querySelector<HTMLElement>('.outline-panel')!;
    this.outline = document.createElement('div'); this.outline.className = 'outline-content';
    for (const child of [...panel.children]) if (!child.classList.contains('pane-resizer')) this.outline.append(child);
    panel.append(this.outline);
    if (enabled) panel.insertAdjacentHTML('afterbegin', '<div class="panel-tabs" role="tablist" aria-label="右侧面板"><button id="outline-tab" role="tab" aria-selected="true">大纲</button><button id="transcription-tab" role="tab" aria-selected="false">转写</button></div>');
    this.root = document.createElement('section'); this.root.id = 'transcription-panel'; this.root.hidden = true; panel.append(this.root);
    if (enabled) {
      document.querySelector<HTMLButtonElement>('#outline-tab')!.onclick = () => this.run(() => this.show(false));
      document.querySelector<HTMLButtonElement>('#transcription-tab')!.onclick = () => this.run(() => this.show(true));
    }
    this.render();
  }
  get dirty() { return Boolean(this.transcript && this.draft !== (this.transcript.editedText ?? this.transcript.text)); }
  assertSaved() { if (this.pending) throw new Error('转写操作尚未完成，请稍后重试。'); if (this.dirty) throw new Error('转写有未保存的修订，请先在转写面板保存或放弃修订。'); }
  reset() { this.generation++; clearTimeout(this.timer); this.target = null; this.transcript = null; this.job = null; this.draft = ''; this.notice = ''; this.pending = false; this.show(false); this.render(); }
  hide() { this.show(false); }
  private show(active: boolean) {
    if (!active) this.assertSaved();
    this.root.hidden = !active; this.outline.hidden = active;
    document.body.classList.toggle('transcription-visible', active);
    if (active) document.body.classList.remove('outline-closed');
    document.querySelector('#outline-tab')?.setAttribute('aria-selected', String(!active));
    document.querySelector('#transcription-tab')?.setAttribute('aria-selected', String(active));
  }
  private run(operation: () => unknown) { Promise.resolve().then(operation).catch(reason => { this.notice = reason instanceof Error ? reason.message : String(reason); this.render(); }); }
  async open(folderId: string, item: WorkspaceRecording, start = false) {
    this.assertSaved();
    const token = ++this.generation; clearTimeout(this.timer);
    this.target = { folderId, id: item.id, name: item.name }; this.job = null; this.transcript = null; this.draft = ''; this.notice = ''; this.show(true); this.pending = true; this.render();
    try {
      const transcript = await request<Transcript | null>('readTranscript', { folderId, id: item.id });
      if (token !== this.generation) return;
      this.transcript = transcript; this.draft = transcript?.editedText ?? transcript?.text ?? '';
      const job = await request<Job | null>('transcriptionStatus');
      if (token !== this.generation) return;
      if (job?.folderId === folderId && job.recordingId === item.id) {
        this.job = job;
        // Completion can race the earlier result read; terminal state is authoritative.
        if (job.state === 'completed') await this.readCompleted(token);
      }
    } finally { if (token === this.generation) { this.pending = false; this.render(); this.schedule(token); } }
    if (token === this.generation && start) await this.start();
  }
  private async start() {
    this.assertSaved(); if (!this.target) return;
    if (this.transcript && !window.confirm('重新转写此录音？已保存的人工修订会保留。')) return;
    const token = this.generation; this.pending = true; this.notice = ''; this.render();
    try { const job = await request<Job>('startTranscription', { folderId: this.target.folderId, id: this.target.id }); if (token === this.generation) this.job = job; }
    finally { if (token === this.generation) { this.pending = false; this.render(); this.schedule(token); } }
  }
  private schedule(token: number) {
    clearTimeout(this.timer);
    if (token !== this.generation || !this.job || !['loading', 'transcribing'].includes(this.job.state)) return;
    this.timer = setTimeout(() => this.run(() => this.poll(token)), 800);
  }
  private async poll(token: number) {
    if (token !== this.generation) return;
    try {
      const job = await request<Job | null>('transcriptionStatus');
      if (token !== this.generation) return;
      if (!job || job.id !== this.job?.id) { this.notice = '当前转写任务已结束，请重新打开录音查看结果。'; this.job = null; this.render(); return; }
      this.job = job;
      if (job.state === 'completed') await this.readCompleted(token);
      if (token !== this.generation) return;
      this.render(); this.schedule(token);
    } catch (reason) { if (token === this.generation) { this.notice = `无法读取转写状态，请重新打开录音重试。${String(reason)}`; this.render(); } }
  }
  private async readCompleted(token: number) {
    if (!this.target) return;
    const result = await request<Transcript | null>('readTranscript', { folderId: this.target.folderId, id: this.target.id });
    if (token !== this.generation) return;
    if (!this.dirty) { this.transcript = result; this.draft = result?.editedText ?? result?.text ?? ''; }
    else this.notice = '转写已完成；当前修订已保留，请先保存。';
  }
  private async cancel() {
    if (this.pending) return;
    const token = this.generation; this.pending = true; clearTimeout(this.timer); this.render();
    try {
      await request('cancelTranscription');
      if (token !== this.generation) return;
      const job = await request<Job | null>('transcriptionStatus');
      if (token !== this.generation) return;
      if (job?.id === this.job?.id) this.job = job;
      else { this.job = null; this.notice = '任务已结束，请重新打开录音查看结果。'; }
      if (this.job?.state === 'completed') await this.readCompleted(token);
    } finally { if (token === this.generation) { this.pending = false; this.render(); this.schedule(token); } }
  }
  private async save() {
    if (!this.target || !this.transcript || this.pending) return;
    const token = this.generation; const text = this.draft; this.pending = true; this.notice = ''; this.render();
    try { const saved = await request<Transcript>('saveTranscript', { folderId: this.target.folderId, id: this.target.id, text, revision: this.transcript.revision }); if (token === this.generation) { this.transcript = saved; this.notice = '修订已保存'; } }
    catch (reason) { if (token === this.generation) this.notice = `保存失败，修订仍在此处。${String(reason)} 如为版本冲突，请先复制修订，再放弃修订并重新打开结果。`; }
    finally { if (token === this.generation) { this.pending = false; this.render(); } }
  }
  async settings() {
    const token = this.generation;
    const settings = await request<Settings>('transcriptionSettings');
    if (token !== this.generation) return;
    const host = document.querySelector<HTMLDialogElement>('#dialog')!;
    document.querySelector('#dialog-title')!.textContent = '本机转写设置';
    document.querySelector('#dialog-content')!.innerHTML = `<form id="transcription-settings"><p class="dialog-note">使用本机 Python 环境与 Qwen3-ASR-0.6B 模型目录。</p><label class="field-label" for="transcription-python">Python 可执行文件</label><input class="name-input" id="transcription-python" required value="${escape(settings.pythonPath)}"><label class="field-label" for="transcription-model">模型目录</label><input class="name-input" id="transcription-model" required value="${escape(settings.modelPath)}"><p id="transcription-settings-message" role="status">${escape(settings.message)}</p><div class="dialog-actions"><button type="submit">保存并检查</button></div></form>`;
    if (!host.open) host.showModal();
    const message = document.querySelector<HTMLElement>('#transcription-settings-message')!;
    document.querySelector<HTMLFormElement>('#transcription-settings')!.onsubmit = event => { event.preventDefault(); const button = document.querySelector<HTMLButtonElement>('#transcription-settings button')!; button.disabled = true; request<Settings>('saveTranscriptionSettings', { pythonPath: document.querySelector<HTMLInputElement>('#transcription-python')!.value.trim(), modelPath: document.querySelector<HTMLInputElement>('#transcription-model')!.value.trim() }).then(result => { message.textContent = result.message || '设置已保存'; }).catch(reason => { message.textContent = String(reason); }).finally(() => button.disabled = false); };
  }
  private render() {
    const focused = this.root.querySelector<HTMLTextAreaElement>('#transcription-text');
    const selection = focused && document.activeElement === focused ? { start: focused.selectionStart, end: focused.selectionEnd } : null;
    const active = Boolean(this.job && ['loading', 'transcribing'].includes(this.job.state));
    const labels = { loading: '正在加载模型', transcribing: '正在转写', completed: '转写完成', failed: '转写失败', canceled: '已取消转写' };
    this.root.innerHTML = `<div class="transcription-heading"><strong>${escape(this.target?.name || '录音转写')}</strong><button id="transcription-config" class="text-button">设置</button></div><p class="transcription-note">${this.target ? '结果独立保存在当前录音下，确认后再插入文档。' : '从录音文件菜单选择“本机转写”或“查看转写”。'}</p>${this.job ? `<p role="status">${labels[this.job.state]}${this.job.state === 'transcribing' && this.job.totalSeconds > 0 ? ` · 已处理 ${Math.floor(this.job.completedSeconds)} / ${Math.ceil(this.job.totalSeconds)} 秒` : ''}</p><p class="transcription-note">${escape(this.job.error || (this.job.state === 'completed' ? '' : this.job.message) || '')}</p>` : ''}${this.notice ? `<p id="transcription-message" role="status">${escape(this.notice)}</p>` : ''}${this.target ? `<div class="transcription-actions"><button id="transcription-start" ${active || this.pending ? 'disabled' : ''}>${this.transcript ? '重新转写' : '本机转写'}</button>${active ? '<button id="transcription-cancel">取消转写</button>' : ''}</div>` : ''}${this.transcript ? `<label class="field-label" for="transcription-text">转写文字</label><textarea id="transcription-text" ${this.pending ? 'readonly' : ''}>${escape(this.draft)}</textarea><p id="transcription-dirty" class="transcription-note">${this.dirty ? '修订尚未保存' : '结果已保存'} · ${escape(this.transcript.model)}</p><div class="transcription-actions"><button id="transcription-save" ${this.pending ? 'disabled' : ''}>保存修订</button><button id="transcription-discard" ${this.pending ? 'disabled' : ''}>放弃修订</button><button id="transcription-insert">插入到文档</button></div>` : this.target && !active && !this.pending ? '<p class="transcription-note">暂无已保存的转写结果。</p>' : ''}`;
    const bind = (id: string, fn: () => unknown) => { const el = this.root.querySelector<HTMLButtonElement>('#' + id); if (el) el.onclick = () => this.run(fn); };
    bind('transcription-config', () => this.settings()); bind('transcription-start', () => this.start()); bind('transcription-save', () => this.save());
    bind('transcription-cancel', () => this.cancel());
    bind('transcription-discard', () => { if (!window.confirm('放弃尚未保存的转写修订？')) return; this.draft = this.transcript?.editedText ?? this.transcript?.text ?? ''; this.notice = ''; this.render(); });
    const input = this.root.querySelector<HTMLTextAreaElement>('#transcription-text');
    if (input && selection) { input.focus({ preventScroll: true }); input.setSelectionRange(selection.start, selection.end); }
    if (input) input.oninput = () => { this.draft = input.value; this.root.querySelector('#transcription-dirty')!.textContent = this.dirty ? '修订尚未保存' : '结果已保存'; };
    const button = this.root.querySelector<HTMLButtonElement>('#transcription-insert');
    if (button) { button.onmousedown = event => event.preventDefault(); button.onclick = () => { try { if (!this.draft.trim()) throw new Error('转写文字为空。'); this.insert(this.draft); } catch (reason) { const message = this.root.querySelector('#transcription-message') || this.root.appendChild(document.createElement('p')); message.textContent = reason instanceof Error ? reason.message : String(reason); } }; }
  }
}
