export type DocumentContent = { header?: unknown[]; main: unknown[]; footer?: unknown[]; [key: string]: unknown };
export interface WorkDocument { id: string; title: string; kind: 'document' | 'template'; updatedAt: string; createdAt?: string; content: DocumentContent }
export interface WorkspaceRecording { id: string; name: string; relativePath: string; durationMs: number; createdAt: string }
export interface WorkspaceFolder { id: string; name: string; documents: WorkDocument[]; recordings: WorkspaceRecording[] }
export interface Workspace { schemaVersion: number; id: string; name: string; path: string; documents: WorkDocument[]; folders: WorkspaceFolder[] }
type NativeResponse = { id?: string; ok?: boolean; result?: unknown; error?: string; event?: string; path?: string };
declare global {
  interface Window {
    chrome?: { webview?: { postMessage(message: unknown): void; addEventListener(type: string, listener: (event: MessageEvent<NativeResponse>) => void): void } };
    recDogReady: Promise<void>;
    recDogSmokeTest: () => Promise<unknown>;
  }
}
const pending = new Map<string, { resolve(value: unknown): void; reject(reason: Error): void; timer: ReturnType<typeof setTimeout> }>();
const events = new EventTarget();
export const isDesktop = Boolean(window.chrome?.webview);
window.chrome?.webview?.addEventListener('message', ({ data }) => {
  if (data.event) events.dispatchEvent(new CustomEvent(data.event, { detail: data }));
  const call = data.id ? pending.get(data.id) : undefined;
  if (!call) return;
  clearTimeout(call.timer);
  pending.delete(data.id!);
  if (data.ok) call.resolve(data.result); else call.reject(new Error(data.error || '文件操作失败，请重试。'));
});
export function request<T>(method: string, payload: unknown = {}): Promise<T> {
  if (!isDesktop) return Promise.reject(new Error('请从听录桌面程序打开，以使用本地文件功能。'));
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    // Native file dialogs may remain open while the user chooses a destination.
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('操作尚未完成，请检查文件对话框后重试。')); }, 600_000);
    pending.set(id, { resolve: value => resolve(value as T), reject, timer });
    window.chrome!.webview!.postMessage({ id, method, payload });
  });
}
export function onHostEvent(name: string, handler: (detail: NativeResponse) => void) {
  events.addEventListener(name, event => handler((event as CustomEvent<NativeResponse>).detail));
}
