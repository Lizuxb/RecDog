# Changelog

## v0.5.0 — 2026-11-03

### 简体中文

- Windows 文档与录音工作台：项目、人员文件夹、文档、模板与 MP3 录音。
- DOCX 模板导入、标题大纲、自动保存，以及文档历史版本预览和恢复。
- 麦克风选择、录音信号方格、暂停与继续；选择录音后手动播放。
- 项目、人员、文档、录音勾选导出；保留目录层级，模板独立选择。
- 阿里云实时识别直接写入文档，支持录音中更换插入位置。
- 关闭、本机、云端识别方式可切换；本机模型仍属实验接入，效果待定。
- 修正缩放时中文光标偏移、错误提示无法关闭、录音导出的 Windows 播放兼容问题。
- Apache-2.0 许可、中英文说明；依赖运行时的 Windows x64 目录包。

已知限制：没有安装器或手机端；本地模型与 Python 环境不随包提供，尚未完成充分效果测试；独立录音后转写入口隐藏；复杂 DOCX 版式可能存在差异；文档草稿集中保存在 workspace.json。

### English

- Windows document and recording workspace with projects, people, documents, templates and MP3 files.
- DOCX template import, heading outlines, autosave, history previews and restoration.
- Microphone selection, input signal squares, pause/resume and explicit recording playback.
- Selective hierarchical export for projects, people, documents and recordings; separate template selection.
- Alibaba Cloud live recognition inserts text directly into documents and supports changing insertion points.
- Off, local and cloud modes; local recognition remains experimental and its quality is undetermined.
- Fixes for Chinese caret drift at fractional zoom, persistent error banners and Windows MP3 export compatibility.
- Apache-2.0 license, Chinese and English documentation, and a Windows x64 folder build requiring runtimes.

Known limits: no installer or mobile app; local models and Python are not bundled or sufficiently evaluated; separate post-recording transcription entries are hidden; complex DOCX formatting may differ; editable drafts are stored together in workspace.json.
