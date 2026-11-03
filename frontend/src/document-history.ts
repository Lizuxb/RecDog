import { request, type WorkDocument } from './bridge';
import { DocumentEditor } from './editor';

type VersionInfo = { id: string; documentId: string; label: string; reason: 'manual' | 'automatic' | 'beforeRestore'; createdAt: string; title: string };
type Version = VersionInfo & { document: WorkDocument };
const esc = (text: string) => text.replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]!);
const reasonLabel = { manual: '手动保存', automatic: '自动检查点', beforeRestore: '恢复前备份' };

/** Project-local history UI; a separate readonly editor cannot mutate the working document. */
export async function showDocumentHistory(document: WorkDocument, restore: (versionId: string) => Promise<void>) {
  const host = window.document.querySelector<HTMLDialogElement>('#dialog')!;
  let preview: DocumentEditor | null = null;
  let selected: Version | null = null;
  let generation = 0, alive = true, pending = false;
  window.document.querySelector('#dialog-title')!.textContent = '历史版本';
  window.document.querySelector('#dialog-content')!.innerHTML = `<section class="history-layout">
    <aside class="history-sidebar"><p class="dialog-note">${esc(document.title)} · 版本保存在当前项目</p>
      <form id="history-create"><label class="field-label" for="history-label">版本名称（选填）</label><input id="history-label" class="name-input" maxlength="100" placeholder="例如：访谈初稿"><button type="submit">保存当前版本</button></form>
      <p class="dialog-note">编辑时最多每 5 分钟保留一次修改前的检查点。恢复前会自动备份当前内容。</p>
      <div id="history-list" role="list" aria-label="文档历史版本"></div>
    </aside>
    <div class="history-main"><p id="history-selected">选择一个版本，查看只读预览。</p><div id="history-preview-scroll"><div id="history-preview"></div></div>
    <p id="history-confirm-note" hidden>恢复会替换当前草稿的标题与正文。当前内容会先保存为“恢复前备份”，录音和其他文档不受影响。</p>
    <div class="dialog-actions"><button id="history-cancel-restore" hidden>取消恢复</button><button id="history-restore" disabled>恢复此版本</button></div></div>
    </section><p id="history-message" role="status"></p>`;
  host.classList.add('history-dialog');
  const $ = <T extends HTMLElement = HTMLElement>(selector: string) => host.querySelector<T>(selector)!;
  const message = (text: string) => { if (alive) $('#history-message').textContent = text; };
  const resetConfirmation = () => {
    $('#history-confirm-note').hidden = true; $('#history-cancel-restore').hidden = true;
    $('#history-restore').textContent = '恢复此版本';
  };
  const updateControls = () => {
    $<HTMLButtonElement>('#history-restore').disabled = pending || !selected;
    $<HTMLButtonElement>('#history-create button').disabled = pending;
    $<HTMLButtonElement>('#close-dialog').disabled = pending;
    $<HTMLButtonElement>('#history-cancel-restore').disabled = pending;
    host.querySelectorAll<HTMLButtonElement>('[data-version]').forEach(button => button.disabled = pending);
  };
  const selectVersion = async (id: string) => {
    const token = ++generation; selected = null; pending = true; resetConfirmation(); updateControls(); message('正在读取历史版本…');
    try {
      const version = await request<Version>('readDocumentVersion', { id: document.id, versionId: id });
      if (!alive || token !== generation) return;
      preview?.destroy(); preview = null; $('#history-preview').replaceChildren();
      preview = new DocumentEditor($<HTMLDivElement>('#history-preview'), version.document.content, true);
      selected = version;
      $('#history-selected').textContent = `${version.label || reasonLabel[version.reason]} · ${new Date(version.createdAt).toLocaleString('zh-CN')} · 只读预览`;
      host.querySelectorAll<HTMLButtonElement>('[data-version]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.version === id)));
      message('');
    } catch (reason) { message(String(reason)); }
    finally { if (alive && token === generation) { pending = false; updateControls(); } }
  };
  const refresh = async () => {
    const versions = await request<VersionInfo[]>('listDocumentVersions', { id: document.id });
    if (!alive) return;
    $('#history-list').innerHTML = versions.length ? versions.map(item => `<button type="button" class="history-version" data-version="${esc(item.id)}" role="listitem" aria-pressed="false"><strong>${esc(item.label || reasonLabel[item.reason])}</strong><time>${esc(new Date(item.createdAt).toLocaleString('zh-CN'))}</time><small>${esc(reasonLabel[item.reason])} · ${esc(item.title)}</small></button>`).join('') : '<p class="dialog-note">暂无历史版本。可以先保存当前版本，之后再编辑。</p>';
    host.querySelectorAll<HTMLButtonElement>('[data-version]').forEach(button => button.onclick = () => { void selectVersion(button.dataset.version!); });
  };
  const onCancel = (event: Event) => { if (pending) event.preventDefault(); };
  const cleanup = () => {
    alive = false; generation++; preview?.destroy(); preview = null;
    host.classList.remove('history-dialog');
    host.removeEventListener('cancel', onCancel);
    $<HTMLButtonElement>('#close-dialog').disabled = false;
  };
  host.addEventListener('cancel', onCancel); host.addEventListener('close', cleanup, { once: true });
  $<HTMLFormElement>('#history-create').onsubmit = async event => {
    event.preventDefault(); if (pending) return;
    pending = true; updateControls();
    try {
      const version = await request<Version>('createDocumentVersion', { id: document.id, label: $<HTMLInputElement>('#history-label').value.trim() });
      await refresh(); pending = false; await selectVersion(version.id); message('当前内容已保存为独立版本。');
    } catch (reason) { message(String(reason)); }
    finally { if (alive) { pending = false; updateControls(); } }
  };
  $('#history-cancel-restore').onclick = resetConfirmation;
  $('#history-restore').onclick = async () => {
    if (pending || !selected) return;
    if ($('#history-confirm-note').hidden) {
      $('#history-confirm-note').hidden = false; $('#history-cancel-restore').hidden = false;
      $('#history-restore').textContent = '确认恢复并备份当前草稿'; return;
    }
    pending = true; updateControls(); message('正在备份并恢复…');
    try { await restore(selected.id); host.close(); }
    catch (reason) { message(String(reason)); }
    finally { if (alive) { pending = false; updateControls(); } }
  };
  if (!host.open) host.showModal();
  try { await refresh(); } catch (reason) { message(String(reason)); }
}
