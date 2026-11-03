import { request, type Workspace, type WorkDocument } from './bridge';
import { DocumentEditor } from './editor';
import { icon } from './icons';

const escape = (value: string) => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
interface ExportSession { sessionId: string; rootName: string; documents: {id: string; relativePath: string; document: WorkDocument}[]; recordingCount: number }
interface ExportResult { path: string; documentCount: number; recordingCount: number }

export async function documentBlob(source: WorkDocument) {
  const holder = document.createElement('div'); holder.className = 'import-staging'; document.body.append(holder);
  let editor: DocumentEditor | undefined;
  try { editor = new DocumentEditor(holder, source.content); return await editor.exportDocx(source.title, false); }
  finally { editor?.destroy(); holder.remove(); }
}

export function blobBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader(); reader.onload = () => resolve(String(reader.result).split(',')[1]); reader.onerror = () => reject(new Error('无法读取导出文档。')); reader.readAsDataURL(blob);
  });
}

export function showExportDialog(workspace: Workspace, _currentId?: string, _selectedKind: 'document' | 'recording' = 'document') {
  const dialog = document.querySelector<HTMLDialogElement>('#dialog')!;
  const content = document.querySelector<HTMLElement>('#dialog-content')!;
  document.querySelector('#dialog-title')!.textContent = '导出项目资料';
  const leaf = (id: string, name: string, kind: 'document' | 'recording') => `<label class="export-leaf"><input type="checkbox" data-export-${kind}="${escape(id)}" >${icon('file')}<span>${escape(name)}</span><small>${kind === 'document' ? 'DOCX' : 'MP3'}</small></label>`;
  const toggle = (target: string, name: string) => `<button type="button" class="export-toggle" data-export-toggle="${target}" aria-expanded="false" aria-label="展开或收起${escape(name)}">›</button>`;
  const templates = workspace.documents.filter(d => d.kind === 'template');
  content.innerHTML = `<p class="dialog-note">勾选需要导出的资料，选择保存位置。保留“项目 / 人员 / 文件”层级；模板单独保存在项目内的“模板导出”文件夹。</p>
    <div id="export-tree" class="export-tree">
      <div class="export-branch">${toggle('export-project-children', workspace.name)}<label class="export-project"><input id="export-project" type="checkbox">${icon('folder')}<strong>${escape(workspace.name)}</strong><small>整个项目（不含模板）</small></label></div>
      <div id="export-project-children" class="export-project-children" hidden>${workspace.documents.filter(d => d.kind !== 'template').map(d => leaf(d.id, d.title, 'document')).join('')}
      ${workspace.folders.map((person, index) => `<section class="export-person"><div class="export-branch">${toggle('export-person-' + index, person.name)}<label class="export-folder"><input type="checkbox" data-export-folder="${escape(person.id)}">${icon('folder')}<span>${escape(person.name)}</span><small>${person.documents.length} 份文档 · ${person.recordings.length} 份录音</small></label></div><div id="export-person-${index}" class="export-folder-children" hidden>${person.documents.map(d => leaf(d.id, d.title, 'document')).join('')}${person.recordings.map(r => leaf(r.id, r.name, 'recording')).join('')}${!person.documents.length && !person.recordings.length ? '<p class="export-empty">空人员文件夹</p>' : ''}</div></section>`).join('')}</div>
      <section class="export-templates"><div class="export-branch">${toggle('export-template-children', '模板导出')}<label><input id="export-templates" type="checkbox" ${templates.length ? '' : 'disabled'}>${icon('folder')}<span>模板导出</span><small>${templates.length} 份模板</small></label></div><div id="export-template-children" class="export-folder-children" hidden>${templates.map(d => leaf(d.id, d.title, 'document')).join('')}${templates.length ? '' : '<p class="export-empty">暂无模板</p>'}</div></section>
    </div>
    <p id="export-summary" class="dialog-note"></p><p id="export-message" role="status" aria-live="polite"></p>
    <div class="dialog-actions"><button id="cancel-export">取消</button><button id="export-selected" class="primary">选择位置并导出</button></div>`;
  const tree = content.querySelector<HTMLElement>('#export-tree')!;
  const root = content.querySelector<HTMLInputElement>('#export-project')!;
  const projectChildren = content.querySelector<HTMLElement>('#export-project-children')!;
  const templateRoot = content.querySelector<HTMLInputElement>('#export-templates')!;
  const templateChildren = content.querySelector<HTMLElement>('#export-template-children')!;
  tree.querySelectorAll<HTMLButtonElement>('[data-export-toggle]').forEach(button => button.onclick = () => { const children = content.querySelector<HTMLElement>('#' + button.dataset.exportToggle)!; children.hidden = !children.hidden; button.setAttribute('aria-expanded', String(!children.hidden)); button.textContent = children.hidden ? '›' : '⌄'; });
  const submit = content.querySelector<HTMLButtonElement>('#export-selected')!;
  const cancel = content.querySelector<HTMLButtonElement>('#cancel-export')!;
  const message = content.querySelector<HTMLElement>('#export-message')!;
  const checkboxes = () => [...tree.querySelectorAll<HTMLInputElement>('input')];
  const leaves = (scope: Element) => [...scope.querySelectorAll<HTMLInputElement>('[data-export-document], [data-export-recording]')];
  const folders = [...tree.querySelectorAll<HTMLInputElement>('[data-export-folder]')];
  function update() {
    for (const folder of folders) {
      const children = leaves(folder.closest('.export-person')!);
      if (children.length) { folder.checked = children.every(c => c.checked); folder.indeterminate = !folder.checked && children.some(c => c.checked); }
    }
    const selectable = [...leaves(projectChildren), ...folders.filter(f => !leaves(f.closest('.export-person')!).length)];
    if (selectable.length) { root.checked = selectable.every(c => c.checked); root.indeterminate = !root.checked && selectable.some(c => c.checked); }
    const templateLeaves = leaves(templateChildren);
    templateRoot.checked = templateLeaves.length > 0 && templateLeaves.every(c => c.checked);
    templateRoot.indeterminate = !templateRoot.checked && templateLeaves.some(c => c.checked);
    const docCount = leaves(tree).filter(c => c.checked && c.dataset.exportDocument).length;
    const recordingCount = leaves(tree).filter(c => c.checked && c.dataset.exportRecording).length;
    content.querySelector('#export-summary')!.textContent = `已选 ${docCount} 份文档、${recordingCount} 份录音 · 导出目录从“${workspace.name}”开始`;
    submit.disabled = !root.checked && !selectable.some(c => c.checked) && !leaves(templateChildren).some(c => c.checked);
  }
  tree.onchange = event => {
    const changed = event.target as HTMLInputElement;
    if (changed === root) for (const box of projectChildren.querySelectorAll<HTMLInputElement>('input')) { box.checked = root.checked; box.indeterminate = false; }
    else if (changed === templateRoot) for (const box of leaves(templateChildren)) box.checked = templateRoot.checked;
    else if (changed.dataset.exportFolder) for (const box of leaves(changed.closest('.export-person')!)) box.checked = changed.checked;
    update();
  };
  let running = false; let cancelRequested = false;
  const close = document.querySelector<HTMLButtonElement>('#close-dialog')!;
  const preventClose = (event: Event) => { if (running) { event.preventDefault(); cancelRequested = true; message.textContent = '正在取消，已导出的临时文件将清理…'; } };
  dialog.addEventListener('cancel', preventClose);
  dialog.addEventListener('close', () => dialog.removeEventListener('cancel', preventClose), {once:true});
  cancel.onclick = () => { if (running) { cancelRequested = true; cancel.disabled = true; message.textContent = '正在取消…'; } else dialog.close(); };
  submit.onclick = async () => {
    if (running) return;
    running = true; cancelRequested = false; close.disabled = true; submit.disabled = true;
    checkboxes().forEach(c => c.disabled = true);
    let session: ExportSession | null = null;
    try {
      message.textContent = '请选择导出位置…';
      const selected = (attribute: 'exportDocument' | 'exportRecording' | 'exportFolder') => checkboxes().filter(c => c.checked && c.dataset[attribute]).map(c => c.dataset[attribute]!);
      session = await request<ExportSession | null>('beginExport', {documentIds:selected('exportDocument'), recordingIds:selected('exportRecording'), folderIds:selected('exportFolder'), includeProject:root.checked});
      if (!session) { message.textContent = '已取消选择保存位置。'; return; }
      for (let i = 0; i < session.documents.length; i++) {
        if (cancelRequested) break;
        const item = session.documents[i];
        message.textContent = `正在导出文档 ${i + 1} / ${session.documents.length}：${item.document.title}`;
        const blob = await documentBlob(item.document);
        if (cancelRequested) break;
        await request('writeExportDocument', {sessionId:session.sessionId, id:item.id, base64:await blobBase64(blob)});
      }
      if (cancelRequested) { await request('cancelExport', {sessionId:session.sessionId}); session = null; message.textContent = '已取消导出。'; return; }
      const result = await request<ExportResult>('finishExport', {sessionId:session.sessionId}); session = null;
      message.textContent = `已导出 ${result.documentCount} 份文档、${result.recordingCount} 份录音。\n保存位置：${result.path}`;
      cancel.textContent = '关闭'; submit.hidden = true;
    } catch (reason) {
      const failure = reason instanceof Error ? reason.message : String(reason);
      if (session) {
        try { await request('cancelExport', {sessionId:session.sessionId}); }
        catch (cleanup) { message.textContent = `导出失败：${failure}\n临时文件清理失败：${cleanup instanceof Error ? cleanup.message : String(cleanup)}`; return; }
      }
      message.textContent = `导出失败：${failure}。请重试。`;
    } finally { running = false; close.disabled = false; cancel.disabled = false; checkboxes().forEach(c => c.disabled = false); templateRoot.disabled = !templates.length; update(); }
  };
  update(); if (!dialog.open) dialog.showModal();
}
