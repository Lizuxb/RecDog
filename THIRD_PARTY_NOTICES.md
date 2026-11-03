# 第三方组件

| 组件 | 版本 | 许可与用途 |
| --- | --- | --- |
| canvas-editor | 1.0.6 | MIT，编辑器 |
| canvas-editor-plugin-docx | 1.0.0 | MIT，DOCX 转换；发布包包含其上游打包实现 |
| NAudio / NAudio.Core / NAudio.WinMM / NAudio.Wasapi / NAudio.Asio / NAudio.Midi / NAudio.WinForms | 2.2.1 | MIT，原生音频采集、Windows Media Foundation 编码 |
| Microsoft.Web.WebView2 SDK | 1.0.2903.40 | 包内 LICENSE.txt 为 BSD 三条款，嵌入式网页组件；共享 Runtime 的分发条款另行适用 |

原始许可随构建输出到程序目录 licenses。NAudio 上游：https://github.com/naudio/NAudio 。编辑器上游：https://github.com/Hufe921/canvas-editor 。

.NET Desktop Runtime 和 WebView2 Runtime 当前由本机提供，不随此目录构建附带；制作完整便携包或安装器时应按相应运行时的分发条款处理。Windows Media Foundation 编码依赖 Windows 内置组件。本表是直接运行组件清单，不替代后续对发布包内打包依赖的完整许可证清单。
