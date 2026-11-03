# RecDog · 听录

[English](README.en.md) · **v0.5.0** · Windows x64 · Apache-2.0

听录是一个 Windows 桌面文档和录音工具。你可以按项目、人员整理文档与录音，导入 DOCX 模板，边录音边编辑，或接入阿里云实时识别，把文字直接写进文档。

程序使用 WPF + WebView2 + canvas-editor。编辑界面随程序在本机运行，不需要打开浏览器，也不需要启动网站服务。关闭转写时，你可以只写文档或只录音。

## v0.5.0 的情况

| 功能 | 当前情况 |
| --- | --- |
| 项目与人员资料 | 新建、重命名、删除、打开项目；点击人员名称进入资料页，小箭头独立展开或收起 |
| 文档编辑 | 字体、字号、标题、正文、列表、表格、查找、自动保存与大纲 |
| 模板 | 内置模板、自建模板、DOCX 导入，支持编辑和删除自建模板 |
| 历史版本 | 保存版本、只读预览、恢复；恢复前备份当前内容 |
| 录音 | 选择麦克风、声音方格、暂停与继续，保存为独立 MP3 |
| 导出 | 勾选项目、人员、文档和录音，保留文件夹层级；模板单独选择 |
| 阿里云实时识别 | 已完成实际账号测试，可以边录音边写入文档；需要网络和你自己的配置 |
| 本地模型识别 | 实验功能。尝试过 sherpa-onnx Streaming Zipformer 和 Qwen3-ASR；目前没有合适模型完成充分测试，准确率、连续性和性能效果待定，不建议依赖它完成正式记录 |
| 录音后独立转写 | 代码保留，界面入口暂时隐藏 |
| 安装与平台 | 目前是依赖运行时的 Windows 目录版；没有安装器，不是包含运行时的完整便携版，暂无手机端 |

本地模型权重和 Python 推理环境不随软件分发。仓库中没有模型权重；文档和录音功能不依赖本地模型。识别文字仍需要你检查，复杂 DOCX 的版式也可能与 Word 不完全一致。

## 下载和打开

在 [Releases](https://github.com/Lizuxb/RecDog/releases) 下载 `RecDog-v0.5.0-windows-x64.zip`，解压整个文件夹后运行 `RecDog.exe`。不要只复制 EXE，`web`、DLL、许可证等文件需要一同保留。

运行前需要：

- Windows x64（建议 Windows 10/11）。
- [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0)。
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

从源码构建后，也可以双击根目录的 `Start-RecDog.cmd`。它打开 `artifacts/app/RecDog.exe`。

## 使用

新建项目默认命名为“项目01、项目02……”，保存在“文档/RecDog/Projects”下；你也可以选择保存位置或打开已有项目。先新建人员文件夹，再创建文档或套用模板。

文档内容自动保存。语义标题会出现在右侧大纲；仅放大字号或加粗的正文不会自动变成标题。点击“历史版本”可以预览和恢复旧内容，恢复前会保存一份当前内容的备份。

录音需要从人员文档中开始。底部可以选麦克风和转写方式，声音方格仅在录音期间显示。录音按照人员名、文档名和时间命名，独立保存在人员目录下；文档与录音不建立固定绑定。点击录音条目只加载播放器，点击播放键才播放。

右上角“导出”支持选择整个项目、人员或单个文件。所有分支默认收拢，全部不勾选。文档导出为 DOCX，录音导出为 MP3：

```text
项目01/
├─ 人员甲/
│  ├─ 访谈记录.docx
│  └─ 人员甲_访谈记录_录音_时间.mp3
└─ 模板导出/
   └─ 访谈模板.docx
```

勾选整个项目不会选中模板，模板在底部分支中独立选择。重名文件和目录会追加序号。录音导出不重编码，只去除可能影响 Windows 播放器的 ID3 标签，项目原文件不变。

错误提示可以关闭；成功切换识别方式会清除相关识别错误，真正的保存失败仍可重试。

## 语音识别

底部提供“关闭转写 / 本机模型 / 阿里云”三种选择。默认关闭转写；录音期间不能切换。关闭时不加载模型，也不上传音频。

### 阿里云

打开“设置 → 实时识别设置”，填写上海地域项目的 AppKey 和临时 Token。Token 留空会保留旧值，过期后需要更新；无需在软件中填写 AccessKey。

打开人员文档，在要插入文字的位置点击后开始录音。中间结果直接写入文档，不需要确认；录音中点击新的位置，后续识别文字会转到那里。识别有处理延迟，位置切换以已经收到的文字为边界。

音频通过 WebSocket 上传到阿里云，使用 16 kHz 单声道 PCM，开启自动标点和数字规范化。费用按你的阿里云账号规则计算。MP3 仍保存在本机，识别不可用时仍可录音。协议见 [阿里云文档](https://help.aliyun.com/zh/isi/user-guide/websocket)。

Token 通过 Windows DPAPI 加密，配置位于 `%LOCALAPPDATA%/RecDog/live-recognition.json`，不保存在项目或源码中。

### 本地模型（实验）

流式接入代码使用 sherpa-onnx，录音后离线转写代码使用 Qwen3-ASR。现阶段没有经过充分测试、可以推荐的本地模型方案，识别效果待定。选择本机模型前需要自行准备 Python 环境和权重；没有模型时可切回仅录音。

如果你要研究现有实现，先安装 [uv](https://docs.astral.sh/uv/)，再运行 `scripts/setup-transcription.ps1`。脚本安装 Python、CPU PyTorch、qwen-asr 和 sherpa-onnx，需要较大磁盘空间。流式模型按以下目录发现：

```text
.tools/qwen-asr-env/Scripts/python.exe
models/sherpa-onnx-streaming-zipformer-bilingual-zh-en/
```

此前测试的模型来源：[Streaming Zipformer](https://www.modelscope.cn/pengzhendong/sherpa-onnx-streaming-zipformer-bilingual-zh-en)、[Qwen3-ASR-0.6B](https://www.modelscope.cn/Qwen/Qwen3-ASR-0.6B)。这些是接入参考，不代表效果保证。独立离线转写设置入口目前隐藏，相关代码保留供后续调整。

## 文件与备份

项目内容保存在 `workspace.json` 中，编辑中的每份文档目前还不是独立 DOCX 文件；需要分享时通过导出生成 DOCX。MP3 是独立文件，历史快照在 `history/` 下。复制整个项目目录可以一起备份文档、历史和录音，单独导出的 DOCX/MP3 文件夹不能代替可继续编辑的完整项目备份。

文档、录音、人员删除时会保留项目 `.trash` 快照；项目删除后移到同级 `.RecDog-trash`。目前没有图形化回收站，恢复前先备份当前项目。历史文件和回收数据不会自动清理。

项目列表和识别配置保存在 `%LOCALAPPDATA%/RecDog`。正常运行的 WebView2 缓存也在那里；项目文件夹由你选择。

## 软件架构

```text
WPF 桌面程序（C# / .NET 10）
├─ WebView2：加载随程序附带的本地界面
│  └─ TypeScript + canvas-editor：文档、模板、大纲和录音操作界面
├─ JSON 消息桥：界面请求 ↔ 本地文件、项目、录音与识别操作
├─ RecDog.Core：项目目录、文档保存、历史版本、导出与路径校验
├─ NAudio + Windows Media Foundation：PCM 采集、MP3 编码
└─ 可选语音识别
   ├─ 阿里云 WebSocket：实时识别
   └─ 独立 Python 进程：本地推理，通过 JSONL / PCM 交换数据
```

编辑器适配集中在 `frontend/src/editor.ts`；界面无需直接访问磁盘。文件和历史逻辑放在 `RecDog.Core`，WPF 负责系统对话框、麦克风和桥接。Python 推理进程与界面进程分开运行，便于替换本地模型。当前仍有集中的工作台和桥接模块，后续会随功能增长继续拆分。

## 从源码开发

需要 .NET 10 SDK、Node.js 24、pnpm（本版使用 11.25.0）和 Windows。安装依赖后构建：

```powershell
pnpm --dir frontend install --frozen-lockfile
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
.\Start-RecDog.cmd
```

`build.ps1` 会构建前端与桌面程序，输出到 `artifacts/app`。本机如果存在 `D:/DevTools/dotnet` 会优先使用，否则使用 PATH 中的 `dotnet`。WebView2 必须在本机安装。

```powershell
dotnet run --project tests/RecDog.Core.Tests
dotnet run --project tests/RecDog.Audio.Tests

# 转写进程测试需要先运行 setup-transcription.ps1 准备 Python
dotnet run --project tests/RecDog.Transcription.Tests
node --test frontend/tests/*.test.mjs
# 浏览器回归需要 Microsoft Edge
node frontend/tests/browser.mjs
# Python 协议测试，不需要下载模型
python -m unittest discover -s workers/qwen_asr -p 'test_*.py'
```

`src/` 是原生代码，`frontend/` 是界面，`workers/` 是可选推理进程，`tests/` 和 `scripts/` 包含测试与构建工具。依赖锁文件与 DOCX 插件补丁保留在仓库中。

源码仓库不包含依赖环境、编译结果、模型、项目资料、录音、凭证或测试缓存。目录版发布包单独放在 Releases。目录构建验证后，运行 `scripts/package.ps1` 生成 ZIP 和 SHA-256 校验文件。公开版本记录见 [CHANGELOG.md](CHANGELOG.md)。

## 许可

RecDog 使用 [Apache License 2.0](LICENSE)。第三方组件保留各自的许可证，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
