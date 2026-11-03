// Run against an isolated --smoke-test directory. Never reads the user's real credentials.
const controlsCall = (method, payload = {}) => new Promise((resolve, reject) => {
  const id = crypto.randomUUID();
  const timer = setTimeout(() => { window.chrome.webview.removeEventListener('message', listener); reject(new Error('controls bridge timeout: ' + method)); }, 15000);
  const listener = ({data}) => { if (data.id !== id) return; clearTimeout(timer); window.chrome.webview.removeEventListener('message', listener); data.ok ? resolve(data.result) : reject(new Error(data.error)); };
  window.chrome.webview.addEventListener('message', listener); window.chrome.webview.postMessage({id, method, payload});
});
const controlsAssert = (value, message) => { if (!value) throw new Error(message); };
controlsAssert((await controlsCall('liveRecognitionSettings')).provider === 'off', 'new installations must default to off');
await controlsCall('saveLiveRecognitionSettings', {provider:'aliyun',appKey:'smoke-app',token:'smoke-token'});
await controlsCall('saveLiveRecognitionSettings', {provider:'local'});
const controlsOff = await controlsCall('saveLiveRecognitionSettings', {provider:'off'});
controlsAssert(controlsOff.provider === 'off' && controlsOff.appKey === 'smoke-app' && controlsOff.hasToken, 'switching modes must retain encrypted cloud credentials');
controlsAssert((await controlsCall('liveRecognitionSettings')).provider === 'off', 'off must persist');
let controlsRejected = false;
try { await controlsCall('saveLiveRecognitionSettings', {provider:'invalid'}); } catch { controlsRejected = true; }
controlsAssert(controlsRejected, 'unknown providers must be rejected');
const controlsWorkspace = await controlsCall('bootstrap');
const controlsPerson = controlsWorkspace.folders.find(f => f.documents.some(d => d.id === result.savedId));
const controlsDoc = controlsPerson.documents.find(d => d.id === result.savedId);
const controlsEvents = [];
const controlsListener = ({data}) => { if (data.event === 'streamingTranscript') controlsEvents.push(data.state); };
window.chrome.webview.addEventListener('message', controlsListener);
try {
  // Deliberately stale streaming=true must still respect the saved off setting, before mic setup.
  // An invalid device avoids recording actual microphone content in this regression test.
  try { await controlsCall('startRecording', {folderId:controlsPerson.id,documentName:controlsDoc.title,deviceId:99999,streaming:true,sessionId:'off-native-guard'}); } catch { }
  await new Promise(resolve => setTimeout(resolve, 100));
  controlsAssert(controlsEvents.length === 0, 'off mode must not start local/cloud recognition or emit ASR errors');
  controlsAssert(!(await controlsCall('recordingStatus')).active, 'failed microphone start must clean up recording state');
} finally { window.chrome.webview.removeEventListener('message', controlsListener); }
result.recognitionControls = {defaultOff:true, providerPersistence:true, credentialsRetained:true, invalidProviderRejected:true, offEnforcedNatively:true};
