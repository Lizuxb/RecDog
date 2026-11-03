# RecDog · 听录

[简体中文](README.md) · **v0.5.0** · Windows x64 · Apache-2.0

RecDog is a Windows desktop tool for documents and audio recordings. You can organize your work by project and person, import DOCX templates, record while editing, and optionally send live audio to Alibaba Cloud for transcription directly into your document.

It uses WPF, WebView2 and canvas-editor. The editor is bundled with the desktop application; you do not need a browser window or a web server. With transcription turned off, you can edit documents or record audio on their own.

## What works in v0.5.0

| Area | Status |
| --- | --- |
| Projects and people | Create, rename, delete and reopen projects. Click a person's name to open their files; use the arrow to expand or collapse the sidebar |
| Documents | Fonts, sizes, headings, paragraphs, lists, tables, search, autosave and outline navigation |
| Templates | Built-in templates, custom templates and DOCX import; edit or delete your own templates |
| Version history | Save snapshots, preview old content and restore it; current content is backed up before a restore |
| Recording | Microphone selection, input signal squares, pause/resume and separate MP3 files |
| Export | Select projects, people, documents or recordings while keeping the folder hierarchy; select templates separately |
| Alibaba Cloud live recognition | Tested with a real account. Requires a network connection and your own configuration |
| Local recognition | Experimental. sherpa-onnx Streaming Zipformer and Qwen3-ASR have been tried, but no suitable model has received enough testing. Accuracy, continuity and performance remain unverified; do not rely on it for final records |
| Separate transcription after recording | Implementation retained; interface entries currently hidden |
| Distribution | Windows folder build requiring installed runtimes. No installer, no fully self-contained portable package, and no mobile app yet |

Model weights and the Python inference environment are not included. Documents and recording work without local models. Check recognized text yourself. Complex DOCX layouts may differ from Microsoft Word.

## Download and run

Download `RecDog-v0.5.0-windows-x64.zip` from [Releases](https://github.com/Lizuxb/RecDog/releases), extract the entire folder, and run `RecDog.exe`. Keep the DLLs, `web` folder and license files with the executable.

Requirements:

- Windows x64; Windows 10/11 recommended.
- [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

After building from source, you can also run `Start-RecDog.cmd` in the repository root. It starts `artifacts/app/RecDog.exe`.

## Working with documents and recordings

New projects use numbered names such as “项目01” and are created under `Documents/RecDog/Projects` by default. You can choose another location or open an existing project. Create a person folder, then create a document or apply a template.

Documents save automatically. Semantic headings appear in the outline; larger or bold text alone does not count as a heading. Use version history to preview and restore earlier content. A backup of the current draft is saved before restoring.

Start recording from a document inside a person folder. Choose a microphone and transcription mode at the bottom. Signal squares appear only during recording. Recordings are separate MP3 files named after the person, the document name at recording start, and the time. Documents and recordings have no fixed binding. Selecting a recording loads it; pressing Play starts playback.

The top-right Export button lets you choose a project, person folder or individual file. All branches start collapsed and nothing is selected. Documents become DOCX files and recordings remain MP3:

```text
Project/
├─ Person/
│  ├─ Interview.docx
│  └─ Person_Interview_Recording_Time.mp3
└─ 模板导出/
   └─ Interview template.docx
```

The template export folder is currently named `模板导出`. Selecting the whole project does not select templates; select them in the separate branch at the bottom. Name collisions get numbered suffixes. Recording export removes ID3 metadata that can cause Windows playback issues, without re-encoding audio or modifying the original.

You can dismiss error messages. Changing recognition mode successfully clears related recognition errors; actual save failures retain their retry action.

## Speech recognition

The recording toolbar offers Off, Local model and Alibaba Cloud. Transcription is off by default. You cannot switch modes while recording. Off does not load a model or upload audio.

### Alibaba Cloud

Open Settings → Live recognition settings and enter an AppKey and temporary Token for a Shanghai-region project. Leaving Token blank keeps the saved value; replace it when it expires. You do not need to enter an AccessKey in RecDog.

Open a person's document, click the insertion point, then start recording. Interim text goes straight into the document without confirmation. Clicking another location during recording moves subsequent recognized text there. Recognition has some latency; the change applies after text already received.

Audio is sent through WebSocket as 16 kHz mono PCM, with punctuation and number normalization enabled. Charges follow your Alibaba Cloud account's terms. The MP3 stays on your computer, and recording remains available if recognition fails. See the [official protocol](https://help.aliyun.com/zh/isi/user-guide/websocket).

The Token is encrypted with Windows DPAPI and stored in `%LOCALAPPDATA%/RecDog/live-recognition.json`, outside your project and source files.

### Local models: experimental

The streaming worker uses sherpa-onnx. The separate offline worker uses Qwen3-ASR. There is no sufficiently tested local model recommendation for this version; recognition quality is still undetermined. Prepare Python dependencies and model weights yourself before selecting Local model. You can switch back to recording only when models are missing.

To investigate the implementation, install [uv](https://docs.astral.sh/uv/) and run `scripts/setup-transcription.ps1`. This installs Python, CPU PyTorch, qwen-asr and sherpa-onnx and needs substantial disk space. Streaming discovery uses:

```text
.tools/qwen-asr-env/Scripts/python.exe
models/sherpa-onnx-streaming-zipformer-bilingual-zh-en/
```

Previously tried models: [Streaming Zipformer](https://www.modelscope.cn/pengzhendong/sherpa-onnx-streaming-zipformer-bilingual-zh-en) and [Qwen3-ASR-0.6B](https://www.modelscope.cn/Qwen/Qwen3-ASR-0.6B). These links document the integrations, not a quality guarantee. Settings for separate offline transcription are currently hidden; their implementation is retained for later work.

## Storage and backups

Document content currently lives in `workspace.json`, rather than a separate DOCX file for each draft. Export creates DOCX files for sharing. MP3 files are independent, and history snapshots live under `history/`. Copy the entire project folder to back up editable documents, history and recordings together. A folder containing exported DOCX/MP3 files is not a full editable project backup.

Deleting documents, recordings or people preserves snapshots in the project's `.trash`; deleting a project moves it to a sibling `.RecDog-trash` folder. There is no graphical trash recovery screen yet. Back up the current project before attempting manual recovery. History and trash are not automatically purged.

The project list, recognition settings and normal WebView2 cache live under `%LOCALAPPDATA%/RecDog`. You choose where project files are stored.

## Architecture

```text
WPF desktop host (C# / .NET 10)
├─ WebView2: bundled local interface
│  └─ TypeScript + canvas-editor: documents, templates, outline and recording controls
├─ JSON message bridge: interface requests ↔ native files, projects, audio and recognition
├─ RecDog.Core: project catalog, persistence, history, export and path checks
├─ NAudio + Windows Media Foundation: PCM capture and MP3 encoding
└─ Optional recognition
   ├─ Alibaba Cloud WebSocket: live recognition
   └─ Separate Python processes: local inference over JSONL / PCM
```

The editor adapter lives in `frontend/src/editor.ts`; the interface does not access the disk directly. `RecDog.Core` handles files and history. WPF provides native dialogs, microphone access and the bridge. Local inference runs separately from the interface so models can be replaced without embedding Python in the UI process. Some workbench and bridge modules are still centralized and will be split further as the application grows.

## Build from source

You need Windows, .NET 10 SDK, Node.js 24 and pnpm (11.25.0 used for this version):

```powershell
pnpm --dir frontend install --frozen-lockfile
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
.\Start-RecDog.cmd
```

The script builds the frontend and desktop application into `artifacts/app`. It uses `D:/DevTools/dotnet` if present, otherwise `dotnet` from PATH. Install WebView2 on the machine separately.

```powershell
dotnet run --project tests/RecDog.Core.Tests
dotnet run --project tests/RecDog.Audio.Tests
# Runner tests require Python from setup-transcription.ps1
dotnet run --project tests/RecDog.Transcription.Tests
node --test frontend/tests/*.test.mjs
# Browser regression requires Microsoft Edge
node frontend/tests/browser.mjs
# Python protocol tests; model weights are not required
python -m unittest discover -s workers/qwen_asr -p 'test_*.py'
```

`src/` contains native code, `frontend/` the interface, `workers/` the optional inference processes, and `tests/` and `scripts/` the tests and build tools. Dependency lockfiles and the small DOCX plugin patch are included.

Dependencies, compiled output, models, project files, recordings, credentials and test caches are excluded from the source repository. Binary folder builds are published separately in Releases. After verifying the build, run `scripts/package.ps1` to create a ZIP and SHA-256 checksum. See [CHANGELOG.md](CHANGELOG.md) for version notes.

## License

RecDog is licensed under the [Apache License 2.0](LICENSE). Third-party components retain their respective licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
