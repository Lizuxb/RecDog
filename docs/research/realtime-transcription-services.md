# 云端实时语音转写服务调研

核验日期：2026-11-02。范围：官方文档与一方 API；未实际调用付费接口，能力可用性、中文准确率与端到端延迟仍需用真实录音验证。仅记录本次已实际抓取并核对的来源，不把未核验品牌列入能力比较。

## 结论

通过云端服务实现“录音时持续显示文字”已有成熟 API 案例。它需要专用音频流接口及供应商适配器，不能仅凭“支持 OpenAI 兼容文本接口”推断可用。普通聊天模型的文字流式响应，也不等于麦克风音频的实时识别。

中文场景可优先验证阿里云 Qwen-ASR-Realtime：官方明确列出中文语言支持与实时草稿/终稿事件。OpenAI 官方也提供完整实时转写协议和示例，但本次抓取的当前实时指南未明确列出中文支持清单，不能据此直接承诺中文能力。火山引擎的一方 API 文档明确描述中文条件下的说话人聚类，但本次取得的是历史文档，应先确认对应现行接口。[O1][A1][A2][V1]

## 能力对照

| 服务 | 实时增量结果 | 中文证据 | 说话人分离/标注 | 音频与连接 | 桌面应用用户自填 API 的适配判断 |
|---|---|---|---|---|---|
| OpenAI Realtime transcription | 音频到达时返回 `conversation.item.input_audio_transcription.delta`；提交话轮后返回 `.completed`。需以 `item_id` 归并结果，不保证不同话轮完成事件顺序。[O1] | 本次当前实时指南与模型页未核验到明确中文清单；需要单独试验。不能拿文件转写旧模型语言支持替代实时模型证据。 | 当前推荐实时模型明确不返回 speaker labels、词级时间戳或置信度。[O1] | 标准 API Key 的服务端 WebSocket：`wss://api.openai.com/v1/realtime?intent=transcription`；官方示例为单声道 PCM16、24kHz，Base64 分块 `input_audio_buffer.append`，客户端判停后 `commit`；另有浏览器 WebRTC 路径。[O1] | 可行：本地桌面后端可按原生协议连接。至少配置凭据、专用实时端点和模型；不能只复用文本聊天地址。此为架构判断，未实测。 |
| 阿里云 Qwen-ASR-Realtime | 高频 `.text` 事件；预览由 `text + stash` 组成，`stash` 可被修正；`.completed.transcript` 是最终结果。[A2] | 客户端文档明确 `zh` 对应普通话、四川话、闽南语、吴语，`yue` 对应粤语。[A1] | 本次客户端/服务端事件页未找到说话人字段证据，不能宣称支持。[A1][A2] | WebSocket；`input_audio_buffer.append` 持续发送；PCM/Opus，16kHz/8kHz（8kHz 由服务端升采样）；可使用 `server_vad` 判停。[A1] | 可行候选：需要专用 WebSocket 和事件适配，按该服务的密钥、区域/端点、模型规则配置；不是把文本兼容端点换一个模型名即可。此为架构判断。 |
| 火山引擎大模型流式识别 | 双向流式 WebSocket；`result_type=single` 为分句增量；优化版二遍识别可提供实时逐字结果及终稿重识别。[V1] | 说话人聚类参数限制明确涉及未指定语言或 `zh-CN`，是中文场景的一方证据。[V1] | `enable_speaker_info=true` 开启说话人聚类；优化版还要求 `enable_nonstream=true`、`ssd_version=200`。这不证明首次增量结果就带稳定说话人标签，仍需实测。[V1] | 文档列 `wss://openspeech.bytedance.com/api/v3/sauc/bigmodel`；16kHz/16bit，PCM/WAV/OGG/MP3。[V1] | 可行候选，但使用前必须核对现行版本、凭据及资源配置；本次来源已归入历史文档。 |

## OpenAI 需要区分的三条路径

1. **实时麦克风流**：实时指南推荐的工作流会在音频到达时产生文字增量；当前示例使用 `gpt-live-transcribe`，客户端负责 VAD 与话轮提交。[O1]
2. **提交话轮后开始识别**：同一实时指南也描述 `gpt-transcribe`：提交音频话轮后才开始识别，然后可产生文字增量，完成事件还可包含检测到的语言。这与边讲话边出现文本的时机不同。[O1]
3. **文件转写**：将已经完成的录音上传到 `/v1/audio/transcriptions`；文件处理时返回文字流并不等于持续接收实时录音。官方文件页列出 MP3、MP4、MPEG、MPGA、M4A、WAV、WebM，当前上限 25MB。[O2]

当前文件页的 `diarized_json` 示例针对专用文件说话人模型，包含 speaker/start/end；不能移植为实时推荐模型已支持说话人标签的结论。该页还列出旧模型弃用与 2027-02-26 关闭说明；实施时应重新查当前模型与迁移说明，避免把本记录中的型号固化为产品能力。[O2]

## 对本地录音应用的具体影响

- **录音归档与实时传输分开**：本地继续保存 MP3 归档；实时适配器从同一采集音频分支重采样/编码成服务所需流格式。OpenAI 示例要求的 PCM16/24kHz，与 MP3 文件归档是不同任务；即使另一家接受 MP3，也不能假设各家相同。[O1][O2][A1][V1]
- **草稿与终稿分开管理**：屏幕显示可修正草稿，完成事件再形成稳定记录。Qwen 明确提供 `stash` 草稿；OpenAI 要求按 `item_id` 匹配终稿。[A2][O1]
- **能力按服务声明**：是否支持实时、中文、实时说话人标签分别判断。缺少说话人能力时允许后续文件处理或人工标注；不能从“支持转写”推出“支持实时分人”。[O1][O2][A2][V1]
- **自填 API 是可行产品方式，但不是统一协议**：用户填写服务选择、凭据和所需端点/区域，桌面本地后端执行相应音频协议。文本 AI 处理和实时转写应分别配置，连接测试分别验证；密钥无需交给产品自建中转。这是基于协议差异的实现建议，尚非运行验证结论。

## 已核验一方来源

- [O1：OpenAI — Realtime transcription](https://developers.openai.com/api/docs/guides/realtime-transcription)：已实际抓取；会话类型、连接地址、增量/完成事件、PCM 示例、VAD、话轮顺序、说话人限制。
- [O2：OpenAI — File transcription](https://developers.openai.com/api/docs/guides/speech-to-text)：已实际抓取；文件格式/大小、文件流式与实时音频的区分、文件说话人标签及弃用说明。
- [A1：阿里云 — Qwen-ASR-Realtime 客户端事件](https://help.aliyun.com/zh/model-studio/qwen-asr-realtime-client-events)：已实际抓取；语言、音频格式、采样率、分块输入及 VAD。
- [A2：阿里云 — Qwen-ASR-Realtime 服务端事件](https://help.aliyun.com/zh/model-studio/qwen-asr-realtime-server-events)：已实际抓取；实时 `text/stash` 与最终 `transcript`。
- [V1：火山引擎 — 大模型流式语音识别 API（历史文档）](https://www.volcengine.com/docs/6561/1354869)：已实际抓取；双向流式、格式、增量返回、二遍识别和说话人参数。历史文档只作为已有一方案例证据，不能直接替代现行接入规范。
