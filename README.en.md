<h1 align="center">RecDog · 听录</h1>

<p align="center">An open-source document and recording tool for Windows</p>

<p align="center">
  <a href="https://github.com/Lizuxb/RecDog/releases"><img src="https://img.shields.io/github/v/release/Lizuxb/RecDog?style=flat-square" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue?style=flat-square" alt="Apache-2.0" /></a>
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Windows" />
</p>

<p align="center">
  <a href="README.md">简体中文</a> · English · <a href="https://github.com/Lizuxb/RecDog/releases">Download</a> · <a href="CHANGELOG.md">Changelog</a>
</p>

## What is RecDog?

RecDog brings document editing, audio recording and speech transcription into one desktop workspace for interviews, meetings and everyday notes. Organize your work by project and person, start with a template, and export documents and recordings when needed.

## Features

- **Organization**: Keep documents and recordings together in project and person folders.
- **Document editing**: Fonts, headings, lists, tables, outline navigation and autosave.
- **Templates**: Import DOCX files and manage reusable templates.
- **Recording**: Select a microphone, pause, resume and play recordings saved as MP3.
- **Live transcription**: Insert Alibaba Cloud recognition results directly into documents, or turn transcription off to edit or record independently. Local model support is experimental; quality is still being evaluated.
- **Version history**: Preview and restore earlier document content.
- **Batch export**: Select projects, people, documents, recordings or templates and export DOCX / MP3 files while preserving the folder hierarchy.

## Quick start

Download the Windows x64 archive from [Releases](https://github.com/Lizuxb/RecDog/releases), extract the entire folder and run `RecDog.exe`.

Requires Windows 10/11, [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) and [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). To use Alibaba Cloud transcription, enter your own AppKey and Token in the application settings.

## Architecture

Built with **WPF + WebView2 + canvas-editor**. C# / .NET handles desktop integration and local files, TypeScript provides the editing interface, and NAudio handles recording. Speech recognition is optional.

To build from source, use Windows with .NET 10 SDK, Node.js 24 and pnpm:

```powershell
pnpm --dir frontend install --frozen-lockfile
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
.\Start-RecDog.cmd
```

## License

RecDog is open source under the [Apache License 2.0](LICENSE). Third-party components retain their own licenses; see [Third-party notices](THIRD_PARTY_NOTICES.md).
