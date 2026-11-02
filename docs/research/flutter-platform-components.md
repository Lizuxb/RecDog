# Flutter 核心组件核验

核验日期：2026-11-02。依据 `docs/requirements-v1.md`；定位是内部文档编辑、DOC/DOCX 导入与 DOCX 导出，并非嵌入完整 Word 编辑器。以下仅采用项目维护者的官方正文，未安装组件或运行性能测试。

## 结论

Flutter 可以进入录音与本机实时转写原型，但现成免费插件尚未证明能覆盖本项目的表格编辑与 Word 兼容要求。决定是否采用 Flutter 的主要门槛应是“Windows 中文输入 + 表格增删合并拆分 + 模板主要格式导入导出”，而不是普通文本框能否显示。移动端复用 UI 不等于音频、DOC 转换及文档兼容自动复用。

## 核心证据

| 组件 | 官方正文实际确认 | 对本项目的限制与建议 |
|---|---|---|
| record | 自述是 **microphone** 录音；Windows 使用 MediaFoundation、Android 使用系统录音接口、iOS 使用 AVFoundation。功能矩阵列出三端暂停/继续、PCM16 文件与流；Windows 支持选择输入设备。[1] | 可作为麦克风 PCM 采集候选。正文没有确认系统播放声音或麦克风混录，不能据此声称已支持。编码矩阵没有 MP3，统一 MP3 存储需独立持续编码链路；四小时、暂停连续性和写盘需实测。 |
| flutter_quill | README 标注 MIT，面向 Android/iOS/Web/桌面；文档用 Delta JSON 表示，提供自定义 embed 接口；官方扩展的现成 embed 为图片与视频。[2] | 此次正文没有证实表格行列增删、合并拆分的完整内置操作。转换章节列 HTML、Markdown、PDF 等第三方转换器，未给出内置免费 DOCX 导入导出。不能把“富文本编辑器”解释为本项目模板已经可用。 |
| super_editor | 官方列 Android/iOS/macOS/Web 为主动验证支持；**Windows/Linux 为 Unverified**。标准编辑器介绍段落、标题、列表、图片、水平线及部分样式，可自定义节点与组件。[3] | Windows 优先项目要增加验证成本。本次正文没有确认完整表格增删合并拆分，也没有证明内置 DOCX 转换。可作为深度定制候选，不宜按开箱即用 Word 组件估算。 |
| sherpa-onnx Flutter | streaming_asr 官方示例明确支持 Windows、Android、iOS 等平台，并给出中英双语流式 Zipformer 模型配置；“online”在此指流式处理，并不必然联网。[4] 通用示例说明原生库随 Flutter 构建链接，每个使用接口的 isolate 必须独立初始化。[5] | 是跨端离线实时 ASR 的明确候选；示例支持不证明中文准确率、四小时稳定性、8GB 电脑或任意手机实时性。需要选定模型测延迟与资源；模型授权逐个核验。推理与有界音频队列应脱离编辑主线程。 |
| FFmpegKit | 原库明确已退休；**2026 年 7 月更新**又说明原作者维护的 FFmpegKitNext 是继续开发的继任者，且 **source only**。原 README 平台列表并未列 Windows；原库默认 LGPL v3，启用 GPL 组件时许可证变化。[6] | 不应继续推荐原包作为长期维护基础，也不能遗漏新继任者。但 source-only 意味着构建、封装、分发仍须评估，不能视为现成全平台解决方案。MP3 编码/音频导入适配器可以逐平台实现；本次没有验证 Next 的完整 Flutter/Windows 能力。 |

## 推荐落实方式

1. 先用 `record → 有界 PCM 队列 → 独立 MP3 编码写盘 / sherpa-onnx 流式转写` 验证 Windows 麦克风链路。系统声音、混音、多声道设备分别做 Windows 原生采集验证，不将它们隐含在 record 支持范围内。[1][4]
2. 表格与 DOCX 是选型否决门槛：拿真实模板验纸张、页边距、页眉页脚、页码、跨页表格、合并拆分与中文输入，再决定是否投入自定义 Flutter 编辑器。当前证据不足以直接确定 flutter_quill 或 super_editor 能免费满足。[2][3]
3. 段落录音来源、稳定段落/单元格 ID、录音目标和确认版本应属于应用自己的文档模型；不要依赖编辑器光标或 DOCX 内部位置充当长期标识。这是依据需求给出的设计建议，不是现成插件能力。
4. DOC/DOCX 转换单独设适配器。以上组件没有证明提供完整免费 Word 转换；音频 FFmpeg 链路也不承担 Word 转换。Windows 可选转换组件并不意味着 Android/iOS 能运行同一可执行程序。旧 DOC 按需求保留可选兼容入口，手机端先明确支持的转换范围；尚未验证前不承诺一个工具覆盖全部平台。
5. 性能验收至少同时进行四小时采集、实时转写、长表格编辑、每两秒保存；依次考察录音完整性、输入响应、推理落后量、内存上限。官方示例无法替代这些测量。[1][4][5]

## 一手来源

[1] record 官方 README（已读取平台/编码/流矩阵及 Usage）：https://github.com/llfbandit/record/blob/master/record/README.md

[2] Flutter Quill 官方 README（已读取 Input/Output、Embed Blocks、Delta Conversion）：https://github.com/singerdmx/flutter-quill/blob/master/README.md

[3] Super Editor 官方 README（已读取 Supported Platforms、标准节点与自定义组件说明）：https://github.com/superlistapp/super_editor/blob/main/super_editor/README.md

[4] sherpa-onnx 官方 Flutter streaming_asr README（已读取支持平台、模型与示例）：https://github.com/k2-fsa/sherpa-onnx/blob/master/flutter-examples/streaming_asr/README.md

[5] sherpa-onnx 官方 Flutter Examples README（已读取初始化、isolate 和平台运行说明）：https://github.com/k2-fsa/sherpa-onnx/blob/master/flutter-examples/README.md

[6] FFmpegKit 官方 README（已读取 July 2026 更新、平台及许可证说明）：https://github.com/arthenica/ffmpeg-kit/blob/main/README.md
