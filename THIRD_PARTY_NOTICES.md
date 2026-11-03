# 第三方组件

| 组件 | 版本 | 许可与用途 |
| --- | --- | --- |
| canvas-editor | 1.0.6 | MIT，编辑器 |
| canvas-editor-plugin-docx | 1.0.0 | MIT，DOCX 转换；发布包包含其上游打包实现 |
| NAudio / NAudio.Core / NAudio.WinMM / NAudio.Wasapi / NAudio.Asio / NAudio.Midi / NAudio.WinForms | 2.2.1 | MIT，原生音频采集、Windows Media Foundation 编码 |
| Microsoft.Web.WebView2 SDK | 1.0.2903.40 | 包内 LICENSE.txt 为 BSD 三条款，嵌入式网页组件；共享 Runtime 的分发条款另行适用 |
| Qwen3-ASR-0.6B | ModelScope commit 4ce9cc7 | Apache-2.0，离线转写模型，用户单独下载 |
| qwen-asr | 0.0.6 | Apache-2.0，Python 推理接口，本机独立安装 |
| sherpa-onnx | 2.0.1 | Apache-2.0，中文流式 ASR 运行库，本机独立安装 |
| sherpa-onnx-streaming-zipformer-bilingual-zh-en | ModelScope pengzhendong | 中英双语流式识别模型；模型包 README 标注 Apache-2.0，用户单独下载 |

Qwen 许可见 `third_party/licenses/Qwen3-ASR-Apache-2.0.txt`。上游：https://www.modelscope.cn/Qwen/Qwen3-ASR-0.6B 。Python 依赖的精确版本见 `workers/qwen_asr/requirements.lock.txt`，CPU PyTorch/torchaudio 为 2.10.0+cpu；这些依赖由安装脚本获取，保留各自上游许可，不随应用目录重新分发。后续若打包推理运行环境，需要补齐其传递依赖许可证清单。

sherpa-onnx 上游及 Apache-2.0 许可证：https://github.com/k2-fsa/sherpa-onnx 。Python wheel 由安装脚本单独安装。中文 Zipformer 上游模型页：https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-transducer/zipformer-transducer-models.html 。用户单独下载模型前应保留该模型包中的 LICENSE/README。

原始许可随构建输出到程序目录 licenses。NAudio 上游：https://github.com/naudio/NAudio 。编辑器上游：https://github.com/Hufe921/canvas-editor 。

DOCX 插件保留 MIT 许可。`frontend/patches/` 中的小补丁为 ESM 导出接口新增 `download: false` 选项，批量导出时获取 DOCX Blob 而不触发逐文件下载；原有转换实现保留。补丁由 pnpm 根据锁文件自动应用。

.NET Desktop Runtime 和 WebView2 Runtime 当前由本机提供，不随此目录构建附带；制作完整便携包或安装器时应按相应运行时的分发条款处理。Windows Media Foundation 编码依赖 Windows 内置组件。本表是直接运行组件清单，不替代后续对发布包内打包依赖的完整许可证清单。
