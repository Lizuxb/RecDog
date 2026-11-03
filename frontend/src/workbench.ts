import { request, onHostEvent, isDesktop, type Workspace, type WorkDocument, type DocumentContent, type WorkspaceFolder } from './bridge';
import { DocumentEditor } from './editor';
import { DocumentState } from './document-state.mjs';
import { documentOwner, tableDimensions, canonicalJson, resourceTime } from './workspace-state.mjs';
import { builtins } from './templates';
import { icon } from './icons';
import { setupPaneResize } from './pane-resize';

const $ = <T extends HTMLElement = HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const esc = (value: string) => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
const dialog = () => $<HTMLDialogElement>('#dialog');
const state = new DocumentState();
let workspace: Workspace;
let current: WorkDocument | null = null;
let folderId: string | null = null;
let collapsedFolder: string | null = null;
let projectOpen = true;
let editor: DocumentEditor | null = null;
let loading = false;
let busy = false;
let saving: Promise<void> | null = null;
let toastTimer: ReturnType<typeof setTimeout>;
let infoTimer: ReturnType<typeof setTimeout>;
let recording: 'idle' | 'recording' | 'paused' | 'saving' = 'idle';
let recordingFolder: string | null = null;
let recordingMilliseconds = 0;
let recordingTick = Date.now();
let recordingOperation: Promise<void> | null = null;
let pendingRecordingSave = false;
let playbackUrl: string | null = null;
let playbackGeneration = 0;
let levelPending = false;
let quietSince = Date.now();
async function refreshMicrophones() {
  const select = $<HTMLSelectElement>('#microphone');
  const previous = select.value;
  const devices = await request<{ id: number; name: string }[]>('recordingDevices');
  select.innerHTML = devices.map(item => '<option value="' + item.id + '">' + esc(item.name) + '</option>').join('') || '<option value="">未检测到麦克风</option>';
  if (devices.some(item => String(item.id) === previous)) select.value = previous;
  select.title = select.selectedOptions[0]?.textContent || '未检测到麦克风';
}
function showLevel(peak: number) {
  const level = Math.max(0, Math.min(1, Number.isFinite(peak) ? peak : 0));
  const lit = recording === 'recording' && level > 0.008 ? Math.ceil(Math.sqrt(level) * 12) : 0;
  $('#input-level').querySelectorAll('i').forEach((square, index) => square.classList.toggle('lit', index < lit));
  $('#input-level').setAttribute('aria-valuenow', String(Math.round(level * 100)));
  if (level > 0.008) quietSince = Date.now();
  $('#input-hint').textContent = recording !== 'recording' ? '' : level >= 0.95 ? '音量过高，可能失真' : Date.now() - quietSince > 5000 ? '输入声音较弱，请检查麦克风' : '';
}
async function pollLevel() {
  if (levelPending || recording !== 'recording') { if (recording !== 'recording') showLevel(0); return; }
  levelPending = true;
  try { const result = await request<{ peak: number }>('recordingLevel'); if (recording === 'recording') showLevel(result.peak); }
  catch { $('#input-hint').textContent = '音量状态暂不可用'; }
  finally { levelPending = false; }
}

function toast(message: string) { $('#toast').textContent = message; $('#toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('#toast').hidden = true, 4200); }
function error(reason: unknown) { $('#save-error span').textContent = reason instanceof Error ? reason.message : String(reason); $('#save-error').hidden = false; }
const action = (operation: () => unknown) => () => { Promise.resolve().then(operation).catch(error); };
function openDialog(title: string, html: string) { $('#dialog-title').textContent = title; $('#dialog-content').innerHTML = html; if (!dialog().open) dialog().showModal(); }
function nameDialog(title: string, initial: string, submit: (name: string) => Promise<void>) {
  openDialog(title, `<form id="name-form"><label class="field-label" for="name-input">名称</label><input id="name-input" class="name-input" maxlength="100" required value="${esc(initial)}"><div class="dialog-actions"><button type="button" id="cancel-name">取消</button><button type="submit" class="primary">保存</button></div><p id="name-error" class="form-error" role="alert"></p></form>`);
  $('#cancel-name').onclick = () => dialog().close();
  $('#name-form').onsubmit = event => { event.preventDefault(); const name = $<HTMLInputElement>('#name-input').value.trim(); if (!name) return; const button = $<HTMLButtonElement>('#name-form [type=submit]'); button.disabled = true; submit(name).then(() => dialog().close()).catch(reason => { $('#name-error').textContent = String(reason.message || reason); }).finally(() => button.disabled = false); };
  $<HTMLInputElement>('#name-input').select();
}
function confirmDelete(title: string, description: string, operation: () => Promise<void>) {
  openDialog(title, `<p class="dialog-note">${esc(description)}</p><div class="dialog-actions"><button id="cancel-delete">取消</button><button class="danger" id="confirm-delete">确认删除</button></div><p id="delete-error" role="alert"></p>`);
  $('#cancel-delete').onclick = () => dialog().close();
  $('#confirm-delete').onclick = () => { const button = $<HTMLButtonElement>('#confirm-delete'); button.disabled = true; operation().then(() => dialog().close()).catch(reason => $('#delete-error').textContent = String(reason.message || reason)).finally(() => button.disabled = false); };
}
function folder() { return workspace.folders.find(item => item.id === folderId); }
function documents() { return folder()?.documents || workspace.documents.filter(item => item.kind === 'document'); }
function allDocuments() { return [...workspace.documents, ...workspace.folders.flatMap(item => item.documents)]; }
function status() { $('#save-label').textContent = !isDesktop ? '布局预览 · 请从桌面程序打开' : saving ? '正在保存…' : !current ? '请选择文档或创建新文档' : state.dirty ? '有未保存的修改' : '已保存'; $('#save-status').classList.toggle('dirty', state.dirty); }
function releasePlayback() { playbackGeneration++; const audio = document.querySelector<HTMLAudioElement>('#recording-player'); if (audio) { audio.pause(); audio.removeAttribute('src'); audio.load(); $('#playback-panel').hidden = true; } if (playbackUrl) URL.revokeObjectURL(playbackUrl); playbackUrl = null; }
function displayTime(value: string) { const date = new Date(value); return Number.isNaN(date.getTime()) ? '时间未知' : date.toLocaleString('zh-CN', { hour12: false }); }
function timeMarkup(value: string) { return `<time datetime="${esc(value)}">${esc(displayTime(value))}</time>`; }
async function playRecording(id: string) {
  requireIdle();
  const owner = folderId;
  const item = folder()?.recordings.find(item => item.id === id);
  if (!owner || !item) return;
  releasePlayback();
  const generation = playbackGeneration;
  const media = await request<{ url: string }>('readRecording', { folderId: owner, id });
  if (generation !== playbackGeneration || owner !== folderId || !projectOpen || recording !== 'idle') return;
  const audio = $<HTMLAudioElement>('#recording-player');
  audio.src = media.url; $('#playback-name').textContent = item.name; $('#playback-panel').hidden = false;
  try { await audio.play(); } catch (reason) { if (generation === playbackGeneration) throw reason; }
}
function recordingActions(id: string) {
  const item = folder()?.recordings.find(item => item.id === id); if (!item) return;
  openDialog('录音文件', `<p class="dialog-note">${esc(item.name)}<br>${esc(displayTime(item.createdAt))}</p><div class="project-actions"><button id="export-recording">导出 MP3</button><button id="reveal-recording">打开所在文件夹</button><button id="copy-recording-path">复制文件路径</button><button id="delete-recording-action" class="danger">删除录音</button></div>`);
  const payload = { folderId, id };
  $('#export-recording').onclick = action(async () => { const result = await request<{ canceled: boolean }>('exportRecording', payload); if (!result.canceled) toast('MP3 已导出'); });
  $('#reveal-recording').onclick = action(() => request('revealRecording', payload));
  $('#copy-recording-path').onclick = action(async () => { await request('copyRecordingPath', payload); toast('录音路径已复制'); });
  $('#delete-recording-action').onclick = () => confirmDelete('删除录音', '删除这份录音？媒体会移入项目 .trash 目录。', async () => { requireIdle(); releasePlayback(); workspace = await request<Workspace>('deleteRecording', payload); render(); });
}
function bindRecordingRows(container: HTMLElement) {
  container.querySelectorAll<HTMLButtonElement>('[data-play]').forEach(button => button.onclick = action(() => playRecording(button.dataset.play!)));
  container.querySelectorAll<HTMLButtonElement>('[data-recording-actions]').forEach(button => button.onclick = () => recordingActions(button.dataset.recordingActions!));
}
function title() {
  $('#document-title').textContent = current?.title || (projectOpen ? folder()?.name || workspace.name : '开始工作');
  $('#paper-name').textContent = current?.title || '';
  $('#scope-name').textContent = current?.title || (projectOpen ? folder()?.name || workspace.name : '未打开项目');
  $('#document-kind').textContent = current ? (current.kind === 'template' ? '项目模板' : `${folder()?.name || '旧版项目文档'} / 文档草稿`) : folder() ? '人员资料' : '本地项目';
  document.title = `${current?.title || '工作台'} · 听录`;
  for (const id of ['rename', 'save', 'save-template', 'export', 'toggle-outline', 'close-current', 'delete-current']) $<HTMLButtonElement>(`#${id}`).disabled = !current;
  $('#workbench-empty').hidden = Boolean(current);
  document.body.classList.toggle('no-document', !current);
}
function render() {
  $('#project-name').textContent = projectOpen ? workspace.name : '未打开项目';
  const resources = [...documents().map(doc => ({ date: resourceTime(doc), html: `<button class="document-item ${doc.id === current?.id ? 'active' : ''}" data-document="${esc(doc.id)}">${icon('file')}<span>${esc(doc.title)}${timeMarkup(doc.createdAt || doc.updatedAt)}</span></button>` })), ...(folder()?.recordings || []).map(item => ({ date: resourceTime(item), html: `<div class="recording-item"><button class="document-item" data-play="${esc(item.id)}" title="播放录音">${icon('file')}<span>MP3 · ${esc(item.name)}${timeMarkup(item.createdAt)}</span></button><button class="icon-button" data-recording-actions="${esc(item.id)}" aria-label="${esc(item.name)}的文件操作" title="导出与文件位置">…</button></div>` }))].sort((a, b) => a.date - b.date);
  const resourceHtml = resources.map(item => item.html).join('') || '<p class="side-empty">暂无资料</p>';
  $('#folder-list').innerHTML = projectOpen ? workspace.folders.map(item => {
    const expanded = item.id === folderId && collapsedFolder !== item.id;
    return `<section class="person-tree"><button class="folder-item ${item.id === folderId ? 'active' : ''}" data-folder="${esc(item.id)}" aria-expanded="${expanded}"><span class="tree-chevron" aria-hidden="true">${expanded ? '⌄' : '›'}</span>${icon('folder')}<span>${esc(item.name)}</span><small>${item.documents.length + item.recordings.length}</small></button>${item.id === folderId ? `<nav id="document-list" class="document-list tree-resources" aria-label="${esc(item.name)}的文档与录音" ${expanded ? '' : 'hidden'}>${resourceHtml}</nav>` : ''}</section>`;
  }).join('') || '<p class="side-empty">新建人员文件夹来整理资料。</p>' : '';
  if (!folderId) $('#folder-list').insertAdjacentHTML('beforeend', '<nav id="document-list" class="document-list tree-resources">' + (projectOpen ? resourceHtml : '') + '</nav>');
  $('#folder-list').querySelectorAll<HTMLButtonElement>('[data-folder]').forEach(button => button.onclick = action(async () => {
    const selected = button.dataset.folder!;
    if (folderId === selected) { collapsedFolder = collapsedFolder === selected ? null : selected; render(); return; }
    requireIdle();
    await guarded(async () => { releasePlayback(); folderId = selected; collapsedFolder = null; closeDocument(); render(); });
  }));
  bindRecordingRows($('#document-list'));
  $('#document-list').querySelectorAll<HTMLButtonElement>('[data-document]').forEach(button => button.onclick = action(() => switchDocument(allDocuments().find(item => item.id === button.dataset.document)!)));
  for (const id of ['new-folder', 'templates', 'import']) $<HTMLButtonElement>(`#${id}`).disabled = !projectOpen || !isDesktop;
  $<HTMLButtonElement>('#new-document').disabled = !folder() || !isDesktop;
  renderLanding(); title(); status(); renderRecording();
}
function renderLanding() {
  const person = folder();
  $('#workbench-empty').innerHTML = !projectOpen ? '<h2>打开一个项目开始工作</h2><p>文档和录音保存在本机，按项目与人员归档。</p><button id="landing-project">打开 / 新建项目</button>' : !person ? `<h2>${esc(workspace.name)}</h2><p>先选择左侧人员文件夹，或新建人员文件夹。</p><div class="landing-actions"><button id="landing-person">新建人员文件夹</button><button id="manage-project">项目管理</button></div>${workspace.documents.some(item => item.kind === 'document') ? '<p>原有文档保留在左侧，可继续打开编辑。</p>' : ''}` : `<div class="folder-heading"><div><h2>${esc(person.name)}</h2><p>${person.documents.length} 份文档 · ${person.recordings.length} 份录音</p></div><div class="landing-actions"><button id="rename-folder">重命名</button><button id="delete-folder" class="danger">删除人员文件夹</button></div></div><div class="landing-actions"><button id="landing-new-document">新建文档</button></div><section class="asset-section"><h3>文档</h3>${person.documents.map(doc => `<div class="asset-row"><button data-open-doc="${esc(doc.id)}">${icon('file')}${esc(doc.title)}</button><button class="quiet danger" data-delete-doc="${esc(doc.id)}">删除</button></div>`).join('') || '<p>暂无文档，从模板创建一份记录。</p>'}</section><section class="asset-section"><h3>录音</h3>${person.recordings.map(item => `<div class="asset-row"><span>${esc(item.name)}<small>${Math.round(item.durationMs / 1000)} 秒 · MP3</small></span><button data-play="${esc(item.id)}">播放</button><button class="quiet danger" data-delete-recording="${esc(item.id)}">删除</button></div>`).join('') || '<p>暂无录音</p>'}</section>`;
  const bind = (id: string, fn: () => unknown) => { const button = document.getElementById(id); if (button) button.onclick = action(fn); };
  bind('landing-project', projectMenu); bind('manage-project', projectMenu); bind('landing-person', newFolder); bind('landing-new-document', templateLibrary);
  bind('rename-folder', () => nameDialog('重命名人员文件夹', person!.name, async name => { await guarded(async () => { await request('renameFolder', { id: person!.id, name }); workspace = await request<Workspace>('bootstrap'); render(); }); }));
  bind('delete-folder', () => confirmDelete('删除人员文件夹', `删除“${person!.name}”及其全部文档、录音？已删除的媒体保留在项目 .trash 目录。`, async () => { requireIdle(); await guarded(async () => { workspace = await request<Workspace>('deleteFolder', { id: person!.id }); releasePlayback(); folderId = null; closeDocument(); render(); }); }));
  $('#workbench-empty').querySelectorAll<HTMLButtonElement>('[data-open-doc]').forEach(button => button.onclick = action(() => switchDocument(person!.documents.find(doc => doc.id === button.dataset.openDoc)!)));
  $('#workbench-empty').querySelectorAll<HTMLButtonElement>('[data-delete-doc]').forEach(button => button.onclick = () => deleteDocument(button.dataset.deleteDoc!));
  bindRecordingRows($('#workbench-empty'));
  $('#workbench-empty').querySelectorAll<HTMLButtonElement>('[data-delete-recording]').forEach(button => button.onclick = () => confirmDelete('删除录音', '删除这份录音？媒体会移入项目 .trash 目录。', async () => { requireIdle(); releasePlayback(); workspace = await request<Workspace>('deleteRecording', { folderId, id: button.dataset.deleteRecording }); render(); }));
}
async function refreshInfo() {
  if (!editor || !current) return;
  const id = current.id; const [count, outline] = await Promise.all([editor.wordCount(), editor.outline()]);
  if (id !== current?.id) return;
  $('#word-count').textContent = `${count.toLocaleString('zh-CN')} 字`; $('#page-count').textContent = `${editor.pages()} 页`;
  const nodes = (items: typeof outline, depth = 0): string => items.map(item => `<button class="outline-item depth-${Math.min(depth, 2)}" data-heading="${esc(item.id)}"><span>${esc(item.name)}</span><small>${item.pageNo + 1}</small></button>${nodes(item.subCatalog || [], depth + 1)}`).join('');
  $('#outline-list').innerHTML = nodes(outline) || '<p class="outline-empty">将段落设置为标题后，<br>会在这里生成大纲。</p>'; $('#outline-count').textContent = String(outline.length || '');
  document.querySelectorAll<HTMLButtonElement>('[data-heading]').forEach(button => button.onclick = () => editor?.locate(button.dataset.heading!));
}
function changed() { if (loading || !current) return; state.changed(); status(); clearTimeout(infoTimer); infoTimer = setTimeout(() => void refreshInfo().catch(error), 500); }
function attachEditor(doc: WorkDocument) {
  editor = new DocumentEditor($('#canvas-editor'), doc.content); editor.onChange = changed; editor.onPages = count => $('#page-count').textContent = `${count} 页`;
  editor.onStyle = style => { for (const name of ['bold', 'italic', 'underline'] as const) $(`[data-action="${name}"]`).classList.toggle('selected', style[name]); $<HTMLSelectElement>('#text-style').value = style.level || ''; $<HTMLSelectElement>('#font-family').value = style.font; const size = $<HTMLSelectElement>('#font-size'); if (![...size.options].some(option => option.value === String(style.size))) size.add(new Option(String(style.size))); size.value = String(style.size); };
}
function closeDocument() { clearTimeout(infoTimer); loading = true; editor?.destroy(); editor = null; current = null; state.load({ id: '' }); loading = false; $('#word-count').textContent = '— 字'; $('#page-count').textContent = '— 页'; $('#outline-list').innerHTML = ''; render(); }
async function saveOnce() {
  if (saving) await saving;
  if (!current || !editor || !state.dirty) return;
  const captured = { ...current, content: editor.serialize() }; const owner = documentOwner(workspace, current.id); const snapshot = state.capture();
  saving = (async () => { const saved = owner ? await request<WorkDocument>('saveFolderDocument', { ...captured, folderId: owner }) : await request<WorkDocument>('saveDocument', captured); const list = owner ? workspace.folders.find(item => item.id === owner)!.documents : workspace.documents; list[list.findIndex(item => item.id === saved.id)] = saved; state.persisted(snapshot); if (current?.id === saved.id) current.updatedAt = saved.updatedAt; $('#save-error').hidden = true; render(); })();
  status(); try { await saving; } finally { saving = null; status(); }
}
async function flush() { while (saving || state.dirty) await saveOnce(); }
async function guarded(operation: () => Promise<void>) { if (busy) throw new Error('正在处理上一项操作，请稍后重试。'); busy = true; document.body.classList.add('busy'); try { await flush(); await operation(); } finally { busy = false; document.body.classList.remove('busy'); } }
async function switchDocument(doc: WorkDocument) { if (current?.id === doc.id) return; await guarded(async () => { closeDocument(); current = structuredClone(doc); state.load(current); loading = true; attachEditor(current); loading = false; render(); await refreshInfo(); }); }
async function createDocument(name: string, content: DocumentContent, kind: 'document' | 'template' = 'document') {
  if (kind === 'document' && !folder()) throw new Error('请先选择或新建人员文件夹。');
  let saved: WorkDocument;
  await guarded(async () => { const payload = { id: crypto.randomUUID(), title: name, kind, content }; saved = kind === 'document' ? await request<WorkDocument>('saveFolderDocument', { ...payload, folderId }) : await request<WorkDocument>('saveDocument', payload); (kind === 'document' ? folder()!.documents : workspace.documents).push(saved); });
  await switchDocument(saved!);
}
function deleteDocument(id: string) { const doc = allDocuments().find(item => item.id === id); if (!doc) return; confirmDelete(`删除${doc.kind === 'template' ? '模板' : '文档'}`, `删除“${doc.title}”？此操作会移除项目中的编辑内容，导入源文件仍保留。`, async () => { await guarded(async () => { workspace = await request<Workspace>('deleteDocument', { id }); if (current?.id === id) closeDocument(); render(); }); }); }
function newFolder() { requireIdle(); nameDialog('新建人员文件夹', '', async name => { await guarded(async () => { const created = await request<WorkspaceFolder>('createFolder', { id: crypto.randomUUID(), name }); workspace.folders.push(created); releasePlayback(); folderId = created.id; collapsedFolder = null; closeDocument(); render(); }); }); }
function templateLibrary() {
  if (!projectOpen) return;
  const custom = workspace.documents.filter(item => item.kind === 'template');
  openDialog('模板库', `<p class="dialog-note">${folder() ? `新文档保存在“${esc(folder()!.name)}”中。` : '请先选择人员文件夹，再使用模板创建文档。'}内置模板固定保留，自建模板可编辑、删除。</p><div class="template-list">${builtins.map(item => `<button class="template-row" data-builtin="${item.id}" ${!folder() ? 'disabled' : ''}>${icon('template')}<span><strong>${item.title}</strong><small>${item.description}</small></span><span class="template-source">内置</span></button>`).join('')}${custom.map(item => `<div class="template-row custom-template">${icon('template')}<span><strong>${esc(item.title)}</strong><small>项目模板</small></span><button data-use-template="${esc(item.id)}" ${!folder() ? 'disabled' : ''}>使用</button><button data-edit-template="${esc(item.id)}">编辑</button><button class="quiet danger" data-delete-template="${esc(item.id)}">删除</button></div>`).join('')}</div><div class="dialog-actions"><button id="import-template">导入 DOCX 模板</button><button id="create-template">自建空白模板</button></div>`);
  document.querySelectorAll<HTMLButtonElement>('[data-builtin]').forEach(button => button.onclick = action(async () => { const item = builtins.find(item => item.id === button.dataset.builtin)!; await createDocument(item.title, item.create()); dialog().close(); }));
  document.querySelectorAll<HTMLButtonElement>('[data-use-template]').forEach(button => button.onclick = action(async () => { const item = custom.find(item => item.id === button.dataset.useTemplate)!; await createDocument(item.title, structuredClone(item.content)); dialog().close(); }));
  document.querySelectorAll<HTMLButtonElement>('[data-edit-template]').forEach(button => button.onclick = action(async () => { await switchDocument(custom.find(item => item.id === button.dataset.editTemplate)!); dialog().close(); }));
  document.querySelectorAll<HTMLButtonElement>('[data-delete-template]').forEach(button => button.onclick = () => deleteDocument(button.dataset.deleteTemplate!));
  $('#import-template').onclick = action(() => importDocument('template')); $('#create-template').onclick = () => nameDialog('新建模板', '未命名模板', name => createDocument(name, builtins[2].create(), 'template'));
}
async function importDocument(kind: 'document' | 'template') {
  if (kind === 'document' && !folder()) throw new Error('请先选择人员文件夹。');
  await flush(); const source = await request<{ name: string; base64: string } | null>('importDocx'); if (!source) return;
  const holder = document.createElement('div'); holder.className = 'import-staging'; document.body.append(holder); let temporary: DocumentEditor | undefined;
  try { temporary = new DocumentEditor(holder, builtins[2].create()); await temporary.importDocx(Uint8Array.from(atob(source.base64), c => c.charCodeAt(0)).buffer); await createDocument(source.name.replace(/\.docx$/i, ''), temporary.serialize(), kind); dialog().close(); compatibility(); } finally { temporary?.destroy(); holder.remove(); }
}
function compatibility() { openDialog('DOCX 格式兼容说明', '<p>采用 canvas-editor 官方 DOCX 插件。适合常用正文、文字格式及表格；请检查复杂表格、字体和分页。修订、批注、复杂域和浮动对象可能不能完整保留。</p><p class="dialog-note">导入源文件保留在项目 sources 目录；草稿保留编辑器原生数据，导出为单独的格式转换。旧版 DOC 请先转换成 DOCX。</p>'); }
function requireIdle() { if (recording !== 'idle') throw new Error('请先停止并保存当前录音。'); }
async function activateProject(method: string, payload: unknown = {}) {
  requireIdle();
  await guarded(async () => {
    const result = await request<Workspace | null>(method, payload); if (!result) return;
    releasePlayback(); closeDocument(); workspace = result; projectOpen = true; folderId = null; collapsedFolder = null; render(); dialog().close();
  });
}
async function newProject() {
  requireIdle();
  const defaults = await request<{ name: string; parent: string }>('projectDefaults');
  let parent = defaults.parent;
  openDialog('新增项目', `<form id="new-project-form"><label class="field-label" for="project-name-input">项目名称</label><input class="name-input" id="project-name-input" maxlength="80" required value="${esc(defaults.name)}"><p class="dialog-note">软件会自动创建同名项目文件夹。</p><p id="project-parent" class="file-path">${esc(parent)}</p><button type="button" id="choose-project-parent">选择保存位置</button><p id="project-create-error" role="alert" class="form-error"></p><div class="dialog-actions"><button type="button" id="cancel-project-create">取消</button><button class="primary" type="submit">创建项目</button></div></form>`);
  $('#cancel-project-create').onclick = action(projectMenu);
  $('#choose-project-parent').onclick = action(async () => { const selected = await request<string | null>('chooseProjectParent'); if (selected) { parent = selected; $('#project-parent').textContent = parent; } });
  $('#new-project-form').onsubmit = event => {
    event.preventDefault(); const button = $<HTMLButtonElement>('#new-project-form [type=submit]'); button.disabled = true;
    activateProject('createProject', { name: $<HTMLInputElement>('#project-name-input').value.trim(), parent }).catch(reason => $('#project-create-error').textContent = reason.message).finally(() => button.disabled = false);
  };
  $<HTMLInputElement>('#project-name-input').select();
}
async function projectMenu() {
  const projects = await request<{ name: string; path: string }[]>('projectList');
  openDialog('项目管理', `<div class="project-list-actions"><button id="create-project" class="primary">新增项目</button><button id="choose-project">打开已有项目</button></div><section class="project-catalog">${projects.length ? projects.map((item, index) => `<button class="project-entry" data-project-index="${index}"><strong>${esc(item.name)}</strong><span>${esc(item.path)}</span>${projectOpen && item.path === workspace.path ? '<small>当前项目</small>' : ''}</button>`).join('') : '<div class="projects-empty"><h3>暂无项目</h3><p>请新增项目，或打开已有项目文件夹。</p></div>'}</section>${projectOpen ? '<div class="project-actions current-project-actions"><p>当前项目：' + esc(workspace.name) + '</p><button id="rename-project">重命名项目</button><button id="close-project">关闭项目</button><button id="delete-project" class="danger">删除项目</button><button id="project-home">返回项目首页</button></div>' : ''}`);
  $('#create-project').onclick = action(newProject); $('#choose-project').onclick = action(() => activateProject('selectWorkspace', { create: false }));
  document.querySelectorAll<HTMLButtonElement>('[data-project-index]').forEach(button => button.onclick = action(() => activateProject('openProject', { path: projects[Number(button.dataset.projectIndex)].path })));
  if (!projectOpen) return;
  $('#rename-project').onclick = () => nameDialog('重命名项目', workspace.name, async name => { requireIdle(); await guarded(async () => { workspace = await request<Workspace>('renameWorkspace', { name }); render(); }); });
  $('#close-project').onclick = action(async () => { requireIdle(); await guarded(async () => { await request('closeWorkspace'); releasePlayback(); closeDocument(); projectOpen = false; folderId = null; render(); }); await projectMenu(); });
  $('#delete-project').onclick = () => confirmDelete('删除项目', `删除“${workspace.name}”及其全部资料？项目会移入同级 .RecDog-trash 文件夹，便于手动恢复。`, async () => { requireIdle(); await guarded(async () => { await request('deleteWorkspace'); releasePlayback(); closeDocument(); projectOpen = false; folderId = null; render(); }); });
  $('#project-home').onclick = action(async () => { await guarded(async () => { releasePlayback(); folderId = null; closeDocument(); render(); }); dialog().close(); });
}

function settings() { void request<{ runtimeVersion: string; appVersion: string; workspacePath: string }>('settings').then(info => { openDialog('设置', `<section class="setting-row"><div><h3>本地项目目录</h3><p>项目包含人员资料、模板、原文件及 MP3 录音。</p></div><div><p class="file-path">${esc(info.workspacePath)}</p><button id="reveal-workspace">在文件夹中查看</button></div></section><section class="setting-row"><div><h3>自动保存</h3><p>每 2 秒保存修改；保存失败时保留当前内容。</p></div><div><strong>RecDog ${esc(info.appVersion)}</strong><p>canvas-editor 1.0.6<br>WebView2 ${esc(info.runtimeVersion)}</p><p>录音仅在本机保存，转写尚未接入。</p></div></section>`); $('#reveal-workspace').onclick = action(() => request('revealWorkspace')); }).catch(error); }
function renderRecording() { $('.input-monitor').hidden = !['recording', 'paused'].includes(recording) || pendingRecordingSave; $<HTMLSelectElement>('#microphone').disabled = recording !== 'idle' || Boolean(recordingOperation); $<HTMLButtonElement>('#refresh-microphones').disabled = recording !== 'idle' || Boolean(recordingOperation); $('#recording-strip').hidden = !projectOpen; $('#recording-state').textContent = pendingRecordingSave ? '录音已生成 · 保存待重试' : recording === 'idle' ? '本地录音 · MP3' : recording === 'paused' ? '录音已暂停' : recording === 'saving' ? '正在处理录音…' : '正在录音'; const elapsed = recordingMilliseconds + (recording === 'recording' ? Date.now() - recordingTick : 0); $('#recording-time').textContent = `${Math.floor(elapsed / 60000).toString().padStart(2, '0')}:${Math.floor(elapsed / 1000 % 60).toString().padStart(2, '0')}`; $<HTMLButtonElement>('#record-start').disabled = recording !== 'idle' || !current || current.kind !== 'document' || !folderId || !folder()?.documents.some(doc => doc.id === current?.id) || Boolean(recordingOperation); $<HTMLButtonElement>('#record-pause').disabled = !['recording', 'paused'].includes(recording) || pendingRecordingSave || Boolean(recordingOperation); $('#record-pause').textContent = recording === 'paused' ? '继续' : '暂停'; $<HTMLButtonElement>('#record-stop').disabled = !['recording', 'paused'].includes(recording) || Boolean(recordingOperation); $('#record-stop').textContent = pendingRecordingSave ? '重试保存录音' : '停止并保存'; $('#recording-strip small').textContent = recordingFolder ? `归属：${workspace.folders.find(item => item.id === recordingFolder)?.name || '人员文件夹'}` : '录音归属开始时的人员文件夹'; }
async function runRecording(operation: () => Promise<void>) {
  if (recordingOperation) throw new Error('正在处理录音，请稍后重试。');
  recordingOperation = operation(); renderRecording();
  try { await recordingOperation; } finally { recordingOperation = null; renderRecording(); }
}
async function startRecording() { requireIdle(); if (!current || current.kind !== 'document' || !folder() || documentOwner(workspace, current.id) !== folderId) throw new Error('请先打开人员文件夹中的具体文档，再开始录音。'); const owner = folderId; const documentName = current.title; recording = 'saving'; renderRecording(); try { await flush(); releasePlayback(); const deviceId = Number($<HTMLSelectElement>('#microphone').value);
    if (!$<HTMLSelectElement>('#microphone').value) throw new Error('请连接并选择麦克风。');
    const started = await request<{ device?: string }>('startRecording', { folderId: owner, documentName, deviceId, deviceName: $<HTMLSelectElement>('#microphone').selectedOptions[0].textContent });
    if (started.device) { $('#microphone').title = started.device; $('#active-microphone').textContent = '录音设备：' + started.device; }
    quietSince = Date.now(); recordingFolder = owner; recording = 'recording'; recordingMilliseconds = 0; recordingTick = Date.now(); } catch (reason) { recording = 'idle'; throw reason; } finally { renderRecording(); } }
async function pauseRecording() { const wasPaused = recording === 'paused'; await request(wasPaused ? 'resumeRecording' : 'pauseRecording'); if (wasPaused) { recording = 'recording'; recordingTick = Date.now(); } else { recordingMilliseconds += Date.now() - recordingTick; recording = 'paused'; } renderRecording(); }
async function stopRecording() { if (recording === 'idle') return; if (recording === 'saving') throw new Error('录音尚在处理中，请稍后关闭。'); if (recording === 'recording') recordingMilliseconds += Date.now() - recordingTick; recording = 'saving'; renderRecording(); try { await request('stopRecording'); workspace = await request<Workspace>('bootstrap'); recording = 'idle'; pendingRecordingSave = false; recordingFolder = null; render(); toast('录音已保存为 MP3'); } catch (reason) { const native = await request<{ active: boolean; paused: boolean; pending?: boolean }>('recordingStatus'); pendingRecordingSave = Boolean(native.pending); recording = pendingRecordingSave ? 'paused' : native.active ? (native.paused ? 'paused' : 'recording') : 'idle'; if (recording === 'recording') recordingTick = Date.now(); throw reason; } finally { renderRecording(); } }

export function startWorkbench() {
  setupPaneResize();
  $('.sheet').insertAdjacentHTML('beforeend', '<section id="playback-panel" class="playback-panel" hidden><span id="playback-name"></span><audio id="recording-player" controls></audio><button id="close-playback" class="quiet" aria-label="关闭播放">关闭播放</button></section>');
  $('#close-playback').onclick = releasePlayback;
  $('.sheet').insertAdjacentHTML('beforeend', '<section class="recording-strip" id="recording-strip"><span id="recording-state">本地录音 · MP3</span><strong id="recording-time">00:00</strong><button id="record-start">开始录音</button><button id="record-pause" disabled>暂停</button><button id="record-stop" disabled>停止并保存</button><label class="microphone-label" for="microphone">麦克风<select id="microphone" aria-label="录音麦克风"><option value="">正在读取设备…</option></select></label><button id="refresh-microphones" title="刷新麦克风列表" aria-label="刷新麦克风列表">刷新</button><span id="active-microphone" class="active-microphone"></span><div class="input-monitor" hidden><div id="input-level" class="signal-squares" role="meter" aria-label="录音声音强度" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i></div><span id="input-hint"></span></div><small>录音归属开始时的人员文件夹</small></section>');
  $('.work-panes').insertAdjacentHTML('beforebegin', '<section class="workbench-empty" id="workbench-empty" hidden></section>');
  $('.heading-actions').insertAdjacentHTML('afterbegin', '<button class="quiet danger" id="delete-current">删除</button>');
  $('#close-dialog').onclick = () => dialog().close(); $('#menu').onclick = () => document.body.classList.toggle('nav-open');
  $('#new-folder').onclick = action(newFolder); $('#new-document').onclick = templateLibrary; $('#templates').onclick = templateLibrary; $('#settings').onclick = settings; $('#compatibility').onclick = compatibility;
  $('#workspace-picker').onclick = action(projectMenu); $('#open-project').onclick = action(projectMenu);
  $('#save').onclick = action(async () => { await flush(); toast('草稿已保存'); }); $('#save-status').onclick = action(flush); $('#retry-save').onclick = action(flush);
  $('#close-current').onclick = action(() => guarded(async () => closeDocument())); $('#delete-current').onclick = () => current && deleteDocument(current.id);
  $('#rename').onclick = () => { if (current) nameDialog('重命名文档', current.title, async name => { current!.title = name; changed(); title(); await flush(); render(); }); };
  $('#save-template').onclick = () => { if (current && editor) nameDialog('另存为模板', `${current.title}模板`, async name => { await flush(); const saved = await request<WorkDocument>('saveDocument', { id: crypto.randomUUID(), title: name, kind: 'template', content: editor!.serialize() }); workspace.documents.push(saved); toast('模板已保存'); }); };
  $('#import').onclick = action(() => importDocument('document')); $('#export').onclick = action(async () => { if (!current || !editor) return; await flush(); await editor.exportDocx(current.title); });
  $('#toggle-outline').onclick = () => { const closed = document.body.classList.toggle('outline-closed'); $('#toggle-outline').setAttribute('aria-expanded', String(!closed)); };
  $('#toggle-search').onclick = () => { if (!editor) return; $('#search-bar').hidden = !$('#search-bar').hidden; if (!$('#search-bar').hidden) $('#find').focus(); }; $('#close-search').onclick = () => { $('#search-bar').hidden = true; editor?.search(''); }; $('#find').oninput = () => editor?.search($<HTMLInputElement>('#find').value); $('#find-next').onclick = () => editor?.nextResult();
  document.querySelectorAll<HTMLButtonElement>('[data-action]').forEach(button => { button.onmousedown = event => event.preventDefault(); button.onclick = action(() => { editor?.action(button.dataset.action!); document.querySelector('details[open]')?.removeAttribute('open'); }); });
  $('#insert-table').onclick = action(() => { const [rows, cols] = tableDimensions($<HTMLInputElement>('#table-rows').value, $<HTMLInputElement>('#table-cols').value); editor?.insertTable(rows, cols); document.querySelector('details[open]')?.removeAttribute('open'); });
  $('#font-family').onchange = () => editor?.font($<HTMLSelectElement>('#font-family').value); $('#font-size').onchange = () => editor?.size(Number($<HTMLSelectElement>('#font-size').value)); $('#text-style').onchange = () => editor?.title($<HTMLSelectElement>('#text-style').value); $('#zoom').onchange = () => editor?.zoom(Number($<HTMLSelectElement>('#zoom').value));
  $('#refresh-microphones').onclick = action(refreshMicrophones);
  $('#microphone').onchange = () => { $('#microphone').title = $<HTMLSelectElement>('#microphone').selectedOptions[0]?.textContent || ''; $('#active-microphone').textContent = ''; };
  setInterval(() => { if (isDesktop) void pollLevel(); }, 150);
  $('#record-start').onclick = action(() => runRecording(startRecording)); $('#record-pause').onclick = action(() => runRecording(pauseRecording)); $('#record-stop').onclick = action(() => runRecording(stopRecording));
  document.addEventListener('keydown', event => { if (event.ctrlKey && event.key.toLowerCase() === 's') { event.preventDefault(); void flush().catch(error); } if (event.ctrlKey && event.key.toLowerCase() === 'f' && current) { event.preventDefault(); $('#search-bar').hidden = false; $('#find').focus(); } });
  onHostEvent('closing', () => { void (async () => { try { if (recordingOperation) await recordingOperation; if (recording !== 'idle') await runRecording(stopRecording); await guarded(async () => { await request('closeReady'); }); } catch (reason) { error(reason); await request('closeAbort'); } })(); });
  onHostEvent('recordingError', event => { if (recording === 'recording') recordingMilliseconds += Date.now() - recordingTick; recording = 'paused'; renderRecording(); error(`录音设备发生错误，请停止保存。${event.error || ''}`); });
  onHostEvent('exported', event => toast(`已导出：${event.path?.split(/[\\/]/).at(-1) || 'DOCX'}`)); onHostEvent('exportCanceled', () => toast('已取消导出')); onHostEvent('exportFailed', event => error(event.error));
  window.recDogReady = (async () => { workspace = isDesktop ? await request<Workspace>('bootstrap') : { schemaVersion: 2, id: 'preview', name: '文档布局预览', path: '', documents: [], folders: [] }; if (isDesktop) projectOpen = (await request<{ open: boolean }>('workspaceStatus')).open; workspace.folders ||= []; if (isDesktop) await refreshMicrophones(); render(); renderRecording(); if (isDesktop && !projectOpen) await projectMenu(); if (!isDesktop) $('#project-name').textContent = '文档布局预览'; setInterval(() => { if (isDesktop && !busy && state.dirty) void saveOnce().catch(error); renderRecording(); }, 2000); setInterval(renderRecording, 1000); })().catch(reason => { error(reason); throw reason; });
  window.recDogSmokeTest = async () => {
    await window.recDogReady;
    const initiallyEmpty = !projectOpen;
    if (initiallyEmpty) {
      const initialList = await request<unknown[]>('projectList');
      if (initialList.length || !document.querySelector('.projects-empty')) throw new Error('首次使用应显示空项目列表');
      await activateProject('createProject', { name: '项目01' });
    }
    const homeMemory = await request('memorySnapshot');
    let playbackVerified = false;
    const originalPath = workspace.path;
    const defaults = await request<{ name: string; parent: string }>('projectDefaults');
    await request('closeWorkspace');
    await request('createProject', { name: defaults.name, parent: defaults.parent });
    const projectList = await request<{ name: string; path: string }[]>('projectList');
    const automaticProjectCreated = projectList.some(item => item.name === defaults.name && item.path.endsWith(defaults.name));
    const numberedDefault = (await request<{ name: string }>('projectDefaults')).name !== defaults.name;
    await request('openProject', { path: originalPath });
    if (!automaticProjectCreated || !numberedDefault) throw new Error('自动项目创建或编号验证失败');
    const mediaPerson = workspace.folders.find(item => item.recordings.length);
    if (mediaPerson) {
      const source = await request<{ url: string }>('readRecording', { folderId: mediaPerson.id, id: mediaPerson.recordings[0].id });
      await request('exportRecording', { folderId: mediaPerson.id, id: mediaPerson.recordings[0].id });
      const audio = new Audio(); audio.muted = true; audio.src = source.url;
      try {
        await Promise.race([new Promise<void>((resolve, reject) => { audio.oncanplay = () => resolve(); audio.onerror = () => reject(new Error('原生录音回放加载失败')); audio.load(); }), new Promise<never>((_resolve, reject) => setTimeout(() => reject(new Error('回放加载超时')), 10000))]);
        await audio.play(); playbackVerified = audio.duration > 1.8; audio.pause();
        if (!playbackVerified) throw new Error('录音时长异常');
      } finally { audio.pause(); audio.removeAttribute('src'); audio.load(); }
    }
    const person = await request<WorkspaceFolder>('createFolder', { id: crypto.randomUUID(), name: '验证人员' }); workspace.folders.push(person); folderId = person.id;
    let documentRequired = false;
    try { await request('startRecording', { folderId: person.id, documentName: '尚未创建的文档' }); } catch { documentRequired = true; }
    if (!documentRequired) throw new Error('未打开文档时不应允许录音');
    await createDocument('桌面验证记录', builtins[0].create()); editor!.insertTable(4, 5); changed(); await flush();
    const reopened = await request<Workspace>('bootstrap'); const saved = reopened.folders.find(item => item.id === person.id)!.documents.find(item => item.id === current!.id); const blob = await editor!.exportDocx(current!.title);
    const result = { savedId: current!.id, roundTripMatch: canonicalJson(saved!.content) === canonicalJson(editor!.serialize()), docxBytes: blob.size, pageCount: editor!.pages(), workspacePath: reopened.path, folderCount: reopened.folders.length, initiallyEmpty, automaticProjectCreated, numberedDefault, documentRequired, playbackVerified, homeMemory, editorMemory: await request('memorySnapshot') };
    if (!result.roundTripMatch || result.docxBytes < 100) throw new Error('文档往返验证失败: ' + JSON.stringify({ result, saved: saved!.content, current: editor!.serialize() })); return result;
  };
}
