// Native WebView2 extension; run only against a dedicated smoke-test project.
const historyCall = (method, payload = {}) => new Promise((resolve, reject) => {
  const id = crypto.randomUUID();
  const timer = setTimeout(() => { window.chrome.webview.removeEventListener('message', listener); reject(new Error('history bridge timeout: ' + method)); }, 15000);
  const listener = ({data}) => { if (data.id !== id) return; clearTimeout(timer); window.chrome.webview.removeEventListener('message', listener); data.ok ? resolve(data.result) : reject(new Error(data.error)); };
  window.chrome.webview.addEventListener('message', listener); window.chrome.webview.postMessage({id, method, payload});
});
const historyWorkspace = await historyCall('bootstrap');
const historyPerson = historyWorkspace.folders.find(f => f.documents.some(d => d.id === result.savedId));
const historyDoc = historyPerson.documents.find(d => d.id === result.savedId);
const historyOriginal = await historyCall('createDocumentVersion', {id: historyDoc.id, label:'原生桥接版本'});
const historyNewContent = {main:[{value:'恢复前的内容'}]};
const historyChanged = await historyCall('saveFolderDocument', {folderId:historyPerson.id, id:historyDoc.id, title:'恢复前的新标题', content:historyNewContent});
const historyRestored = await historyCall('restoreDocumentVersion', {id:historyDoc.id, versionId:historyOriginal.id, expectedUpdatedAt:historyChanged.updatedAt});
if (historyRestored.title !== historyDoc.title || JSON.stringify(historyRestored.content) !== JSON.stringify(historyDoc.content)) throw new Error('Native restore mismatch');
const historyList = await historyCall('listDocumentVersions', {id:historyDoc.id});
const historyBackup = historyList.find(v => v.reason === 'beforeRestore');
if (!historyBackup) throw new Error('Native restore backup missing');
const historyBackupDocument = await historyCall('readDocumentVersion', {id:historyDoc.id, versionId:historyBackup.id});
if (historyBackupDocument.document.title !== '恢复前的新标题') throw new Error('Native restore backup changed');
let historyConflictRejected = false;
try { await historyCall('restoreDocumentVersion', {id:historyDoc.id, versionId:historyOriginal.id, expectedUpdatedAt:historyChanged.updatedAt}); } catch { historyConflictRejected = true; }
if (!historyConflictRejected) throw new Error('Native restore conflict not rejected');
result.history = {snapshotCount:historyList.length, restored:true, beforeRestorePreserved:true, conflictRejected:true};
