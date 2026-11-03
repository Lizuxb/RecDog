// Optional real-model WebView2 smoke extension. Use only with an isolated fixture project.
const call = (method, payload = {}) => new Promise((resolve, reject) => {
  const id = crypto.randomUUID();
  const timeout = setTimeout(() => { window.chrome.webview.removeEventListener('message', listener); reject(new Error('ASR bridge timeout: ' + method)); }, 20000);
  const listener = ({data}) => {
    if (data.id !== id) return;
    clearTimeout(timeout); window.chrome.webview.removeEventListener('message', listener);
    data.ok ? resolve(data.result) : reject(new Error(data.error));
  };
  window.chrome.webview.addEventListener('message', listener);
  window.chrome.webview.postMessage({id, method, payload});
});
const project = await call('bootstrap');
const person = project.folders.find(f => f.recordings.length);
if (!person) throw new Error('ASR smoke requires a fixture recording');
const target = {folderId: person.id, id: person.recordings[0].id};
if (!(await call('transcriptionSettings')).ready) throw new Error('ASR runtime not configured');
const wait = async () => {
  for (let i = 0; i < 140; i++) {
    const job = await call('transcriptionStatus');
    if (job && !['loading', 'transcribing'].includes(job.state)) {
      if (job.state !== 'completed') throw new Error(JSON.stringify(job));
      return job;
    }
    await new Promise(r => setTimeout(r, 400));
  }
  throw new Error('ASR inference timeout');
};
await call('startTranscription', target);
await wait();
const original = await call('readTranscript', target);
if (!original || !/[\u4e00-\u9fff]/.test(original.text)) throw new Error('No real Chinese result');
const edited = await call('saveTranscript', {...target, text: '人工核对保留', revision: original.revision});
await call('startTranscription', target);
await wait();
const rerun = await call('readTranscript', target);
if (rerun.editedText !== edited.editedText || rerun.revision !== edited.revision + 1) throw new Error('Rerun lost saved edit');
await call('startTranscription', target);
const start = performance.now();
await call('closeWorkspace');
if (await call('transcriptionStatus') !== null) throw new Error('Project close retained job');
await call('openProject', {path: project.path});
const reopened = await call('readTranscript', target);
if (reopened.editedText !== edited.editedText || reopened.revision !== rerun.revision) throw new Error('Canceled rerun changed saved transcript');
result.transcription = {text: original.text, editedText: reopened.editedText, revision: reopened.revision, rerunPreservedEdit: true, projectCloseCanceled: true, cancelMilliseconds: Math.round(performance.now() - start)};
