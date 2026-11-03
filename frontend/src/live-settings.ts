import { request } from './bridge';

export type RecognitionProvider = 'off' | 'local' | 'aliyun';
export type LiveSettings = { provider: RecognitionProvider; appKey: string; hasToken: boolean };
export const recognitionOptions = '<option value="off">关闭转写 · 手动编辑 / 仅录音</option><option value="local">本机模型 · 离线识别</option><option value="aliyun">阿里云 · 实时识别</option>';
export async function showLiveSettings(onSaved: (settings: LiveSettings) => void = () => {}) {
  const settings = await request<LiveSettings>('liveRecognitionSettings');
  const host = document.querySelector<HTMLDialogElement>('#dialog')!;
  document.querySelector('#dialog-title')!.textContent = '实时识别设置';
  document.querySelector('#dialog-content')!.innerHTML = `<form id="live-settings">
    <label class="field-label" for="live-provider">识别方式</label>
    <select id="live-provider" class="name-input">${recognitionOptions}</select>
    <p class="dialog-note">关闭后可自行编辑，也可只保存 MP3 录音；不会加载模型或发送录音到云端。本机识别需另行配置本机模型。</p>
    <section id="aliyun-fields">
      <p class="dialog-note">上海地域 · 普通话 16K。录音音频会发送至阿里云，费用按你的阿里云账号计收；MP3 仍保存在本机。</p>
      <label class="field-label" for="live-appkey">项目 AppKey</label><input id="live-appkey" class="name-input" autocomplete="off" maxlength="256">
      <label class="field-label" for="live-token">临时 Token</label><input id="live-token" type="password" class="name-input" autocomplete="off" maxlength="8192">
      <p id="live-token-hint" class="dialog-note"></p>
      <label class="live-token-clear"><input type="checkbox" id="live-clear-token">清除已保存的 Token</label>
      <p class="dialog-note">Token 加密保存在当前 Windows 用户下，不进入项目文件。Token 到期后请重新填写；无需填写 AccessKey。</p>
    </section>
    <p id="live-settings-message" role="status"></p>
    <div class="dialog-actions"><button type="submit">保存设置</button></div>
  </form>`;
  const provider = document.querySelector<HTMLSelectElement>('#live-provider')!;
  const key = document.querySelector<HTMLInputElement>('#live-appkey')!;
  const token = document.querySelector<HTMLInputElement>('#live-token')!;
  provider.value = settings.provider; key.value = settings.appKey;
  token.placeholder = settings.hasToken ? '已保存；留空保留，填写则替换' : '粘贴有效的临时 Token';
  document.querySelector('#live-token-hint')!.textContent = settings.hasToken ? '已保存 Token，保存状态不代表 Token 尚未过期。' : '尚未配置 Token。';
  const toggle = () => document.querySelector<HTMLElement>('#aliyun-fields')!.hidden = provider.value !== 'aliyun';
  provider.onchange = toggle; toggle();
  document.querySelector<HTMLFormElement>('#live-settings')!.onsubmit = async event => {
    event.preventDefault();
    const button = document.querySelector<HTMLButtonElement>('#live-settings button')!;
    const message = document.querySelector('#live-settings-message')!;
    button.disabled = true;
    try {
      const saved = await request<LiveSettings>('saveLiveRecognitionSettings', { provider: provider.value, appKey: key.value, token: token.value, clearToken: document.querySelector<HTMLInputElement>('#live-clear-token')!.checked });
      onSaved(saved);
      document.querySelector('#live-token-hint')!.textContent = saved.hasToken ? '已保存 Token，保存状态不代表 Token 尚未过期。' : '尚未配置 Token。';
      token.placeholder = saved.hasToken ? '已保存；留空保留，填写则替换' : '粘贴有效的临时 Token';
      document.querySelector<HTMLInputElement>('#live-clear-token')!.checked = false;
      token.value = ''; message.textContent = '设置已保存，下次开始录音时生效。';
    } catch (reason) { message.textContent = reason instanceof Error ? reason.message : String(reason); }
    finally { button.disabled = false; }
  };
  if (!host.open) host.showModal();
}
