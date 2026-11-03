<h1 align="center">RecDog · 听录</h1>

<p align="center">开源的 Windows 文档与录音工具</p>

<p align="center">
  <a href="https://github.com/Lizuxb/RecDog/releases"><img src="https://img.shields.io/github/v/release/Lizuxb/RecDog?style=flat-square" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue?style=flat-square" alt="Apache-2.0" /></a>
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Windows" />
</p>

<p align="center">
  简体中文 · <a href="README.en.md">English</a> · <a href="https://github.com/Lizuxb/RecDog/releases">下载</a> · <a href="CHANGELOG.md">更新记录</a>
</p>

## 听录是什么？

听录把文档编辑、录音和语音转写放在同一个桌面工作台中，适合访谈、会议和日常记录。你可以按项目和人员整理资料，使用模板开始记录，再按需要导出文档与录音。

> 项目仍在开发中，各项功能还需经过多轮测试与完善。欢迎试用并反馈问题，重要资料请做好备份。

## 主要功能

- **资料管理**：按项目、人员文件夹整理文档和录音。
- **文档编辑**：支持字体、标题、列表、表格、大纲导航和自动保存。
- **模板库**：导入 DOCX 模板，创建和管理常用模板。
- **录音**：选择麦克风，暂停、继续和播放，保存为 MP3。
- **实时转写**：接入阿里云，将识别文字直接写入文档；也可以关闭转写，仅编辑或录音。本地模型为实验性支持，效果待定。
- **历史版本**：预览和恢复文档的历史内容。
- **批量导出**：勾选项目、人员、文档、录音或模板，导出 DOCX / MP3 并保留目录层级。

## 快速开始

从 [Releases](https://github.com/Lizuxb/RecDog/releases) 下载 Windows x64 压缩包，完整解压后运行 `RecDog.exe`。

需要 Windows 10/11、[.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) 和 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。使用阿里云转写时，在软件设置中填写你自己的 AppKey 和 Token。

## 技术架构

使用 **WPF + WebView2 + canvas-editor**：C# / .NET 负责桌面与本地文件，TypeScript 负责编辑界面，NAudio 负责录音；语音识别按需接入。

源码构建需要 Windows、.NET 10 SDK、Node.js 24 和 pnpm：

```powershell
pnpm --dir frontend install --frozen-lockfile
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
.\Start-RecDog.cmd
```

## 许可证

听录使用 [Apache License 2.0](LICENSE) 开源。第三方组件遵循各自的许可证，详见 [第三方组件说明](THIRD_PARTY_NOTICES.md)。
