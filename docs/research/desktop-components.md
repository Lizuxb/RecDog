# Windows 桌面文档组件与部署核验

调研日期：2026-11-02。依据：`../requirements-v1.md` 及用户最新澄清。DOC/DOCX 是导入格式，导入后编辑软件自己的内容模型，最终导出 DOCX，正文、表格和主要格式变化不大即可；不要求直接编辑 Word 文件、逐像素分页或无损往返。用户优先选择免费开源组件，接受更多开发与兼容性处理。

## 建议

采用 **WPF + 受目标 Windows 支持的 .NET LTS + Open XML SDK + 软件自己的文档模型与编辑层**。优先评估 .NET 10，但先具体确认 Windows 版本范围。商业办公编辑控件与完整办公套件均不是这条路线的必要组件。

建议的数据流为：

```text
DOC → 可选 LibreOffice 转换 → DOCX
DOCX → Open XML 导入 → 内部文档模型 ↔ WPF 编辑层
                              ↓
                      Open XML 生成 DOCX
```

这是基于下述组件边界提出的工程方案，不代表已经实现转换器或完成模板验收。基本格式映射、表格操作、内容控件到可填写区域的转换、录音目标与段落来源维护，都属于应用开发工作。无需把原始 DOCX 包定向修改作为主方案，也不能因为采用内部模型而省略需求中的表格合并拆分、页眉页脚、页码和兼容性报告。

## 已核验的组件边界

| 组件 | 官方证据与结论 | 对本项目的含义 |
| --- | --- | --- |
| WPF | Microsoft 明确说明：即使 .NET 跨平台，WPF 也只在 Windows 上运行。[S1] WPF 官方仓库采用 MIT 许可证，分发须保留相应版权与许可声明。[S2] | 适合当前 Windows 首版；未来 macOS 需另做界面与平台适配。 |
| WPF RichTextBox / FlowDocument | `TextRange.Load` 与 `Save` 当前支持的格式均只有 `Rtf`、`Text`、`Xaml`、`XamlPackage`，不包括 DOCX。[S3][S4] | 可以复用内部编辑能力；DOCX 导入/导出必须另写映射层。不能把原生 RichTextBox 称为现成 DOCX 编辑器。表格合并拆分与业务撤销/重做是否足够方便，应由原型验证。 |
| Open XML SDK | 官方 README 将其定位为 OPC 包、Open XML 标记及强类型类的低层 API，并明确不直接提供高层抽象或生产力工具。[S5] Microsoft 文档同样说明它操作 ZIP/XML 包及架构元素。[S6] | 用于读写 DOCX，不提供编辑界面或 Word 排版引擎。可将导入结果转换为内部模型，再从内部模型生成 DOCX；不需要 Word 自动化。 |
| Open XML SDK 授权与性能 | 官方 LICENSE 为 MIT，允许使用、修改与分发，要求保留版权和许可声明。[S7] README 还记录：.NET Core/.NET 5+ 的 ZIP 包不能流式处理，某些情形内存可能大幅增长。[S5] | 免费开源路线可用；不能将大文件读写当成天然低内存。导入/导出在后台处理，并用长文档测试内存。 |
| 内容控件 | Open XML SDK 的 `SdtBlock` 对应 `w:sdt`，其 API 具有 `SdtContentBlock` 等结构。[S8] | 能读取结构不等于拥有可交互的内容控件编辑 UI。应用需识别类型、锁定状态和填写语义，再映射为自己的可填写区域；不支持的控件必须报告。 |

内部文档模型应独立持久化段落、文字样式、列表、表格网格及合并关系、页面参数、页眉页脚/页码信息、稳定块编号与录音来源。不要只把控件当前显示内容当作全部文档数据。此处是设计建议，不是 SDK 自动完成的功能。

## .NET 10 与安装版、便携版

- .NET 10 是 LTS，官方支持策略列出的支持结束时间为 **2028-11-14**；仍需持续采用受支持的补丁版本。[S9]
- 当前 Microsoft Windows 安装页明确写出：**Windows 10 支持仅限 LTSC 和 Enterprise 版本**。其 .NET 10 支持表列出 Windows 10 `21H2 / 1809 / 1607 LTSC/Enterprise`，以及所列受支持 Windows 11 版本。因此不能把需求中的“Windows 10/11 64 位”直接等同于“.NET 10 官方支持全部 Windows 10”。[S10] 普通 Windows 10 上能否实际运行、是否在 Microsoft 官方支持范围内，应分开说明；后续须按目标机器的版本与生命周期锁定基线。
- WPF 项目使用 Windows 专用目标框架和 `UseWPF=true`；官方项目说明明确启用 WPF 后只能面向 Windows。[S11] 原型可采用 `net10.0-windows`，但最终运行时版本仍受上述支持基线约束。
- **自包含发布**将所需 .NET 文件随应用交付，目标电脑不需要先安装对应 .NET 运行时；它不包含所有操作系统原生依赖，并且体积更大。自包含应用中的运行时补丁需随新版应用交付。[S12] 本项目宜先做 `win-x64` 自包含目录包，安装版和便携版复用发布产物。
- 自包含、单文件、便携运行是不同概念。单文件可以是自包含或依赖运行时，且始终限定操作系统/架构；原生库和依赖文件路径可能带来额外限制。[S13] 第一版便携包可以是完整目录，无需强求单个 EXE；模型、项目与可选转换工具也不必塞入单文件。
- Microsoft 明确说明 WPF 不兼容裁剪，SDK 当前禁用其裁剪支持。[S14] 因此不要把 `PublishTrimmed` 当作缩小便携包的默认办法。

建议原型明确使用 `dotnet publish -c Release -r win-x64 --self-contained true`，再在未安装开发工具、Word/WPS 和单独 .NET 运行时的目标系统测试。此命令是发布建议，本次未执行构建或安装。

## LibreOffice 只作为旧 DOC 的可选转换依赖

官方启动参数文档确认 `--headless` 无界面运行、`--convert-to` 格式转换、`--outdir` 输出目录、`--accept` UNO 连接，以及通过 `-env:UserInstallation=...` 指定用户配置目录；LibreOffice 需要对该配置目录具有写权限。[S15] 官方过滤器表列出 DOCX 对应的 `Office Open XML Text` 等过滤器。[S16]

因此可以实现隔离的本机转换进程：旧 DOC 输入、输出到临时目录、成功校验后再导入，始终保留原文件。转换工具不可用时提示用户先转换为 DOCX，不阻断 DOCX 导入、编辑和导出。**本次只核实接口存在，未运行任何 DOC→DOCX 转换，也未验证便携部署、转换成功率或模板排版。**

LibreOffice 官方许可页说明以 MPL 2.0 提供，并包含其他开源许可的软件；实际安装包的 LICENSE 需按所分发版本核对。MPL 对可执行形式分发涉及源代码获取方式等条件，并不等于无条件打包。[S17] 不将 LibreOffice 的 UNO/headless 能力推断为可直接嵌入 WPF 的现成编辑控件；本方案也不需要做此集成。

## 商业与完整办公引擎仅作背景

Telerik 官方文档证实 RadRichTextBox 能导入/导出 DOCX 内容控件，支持多种控件类型和锁定编辑行为；这属于专用编辑组件能力，不是 WPF 自带能力。[S18] DevExpress 产品页列出 DOC/DOCX、表格及页眉页脚等能力，但本次其详细文档入口受到 Cloudflare 验证阻挡，不能据此确认全部内容控件与表格细项。[S19] Syncfusion 官方 WPF 概览确认 DOC/DOCX 导入导出和表格等能力，同时明确 **SfRichTextBoxAdv 当前不能编辑页眉页脚中的富文本**。[S20] 这些产品在当前路线中均非必需，也未据此作采购或免费开源判断。

ONLYOFFICE Desktop Editors 官方 README 确认离线 DOCX 编辑，同时列出 `desktop-apps`、`desktop-sdk`、`core`、`sdkjs`、`web-apps` 等组件，不能当成一个直接替换 RichTextBox 的 WPF 控件。仓库许可证是 AGPLv3，并有附加署名、修改声明、界面许可通知及非代码资源条款；采用或分发须按实际组合方式检查许可范围。[S21][S22] 当前没有必要引入这一整套引擎。

## 最小验证清单

1. 收集真实模板，完成 DOCX→内部模型→编辑→DOCX；比较正文、字体字号、基本样式、列表、表格结构，以及纸张、页边距、页眉页脚、页码和跨页设置。以用户认可的“格式变化不大”为验收口径，不要求无损往返。
2. 验证空白文档、增删表格行列、横纵合并与拆分、内容控件填写、撤销/重做；同时验证录音目标删除、合并拆分后来源关联及后台追加不覆盖人工修改。
3. 验证长文档与两秒自动保存并行时的编辑响应、内存和导出耗时；自动保存内部文档，不需要每两秒生成 DOCX。
4. 在实际 Windows 版本的干净机器测试自包含目录包与便携设置目录写权限；旧 DOC 转换作为独立兼容性用例，单测缺失组件、超时、失败及临时文件清理。

## 实际获取的一方来源

以下正文均在本次调研实际获取；链接及主分支内容可能变化，应在实施时锁定版本。本次没有安装组件、运行软件、购买许可或完成模板实测。默认网络访问最初被系统阻止，随后通过获准的只读联网读取以下资料；HTTP 成功但只有挑战页/空正文的入口未作为已证实能力。

- [S1：Microsoft WPF 概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)。
- [S2：WPF 官方 MIT LICENSE](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)。
- [S3：TextRange.Load 官方 API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.documents.textrange.load?view=windowsdesktop-10.0)。
- [S4：TextRange.Save 官方 API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.documents.textrange.save?view=windowsdesktop-10.0)。
- [S5：Open XML SDK 官方 README](https://github.com/dotnet/Open-XML-SDK/blob/main/README.md)。
- [S6：Microsoft Open XML SDK 概览](https://learn.microsoft.com/en-us/office/open-xml/open-xml-sdk)。
- [S7：Open XML SDK MIT LICENSE](https://github.com/dotnet/Open-XML-SDK/blob/main/LICENSE)。
- [S8：SdtBlock 官方 API](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtblock?view=openxml-3.0.1)。
- [S9：Microsoft .NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。
- [S10：Microsoft .NET Windows 安装与支持表](https://learn.microsoft.com/en-us/dotnet/core/install/windows)。
- [S11：.NET Desktop SDK 项目属性](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop)。
- [S12：.NET 应用发布概览](https://learn.microsoft.com/en-us/dotnet/core/deploying/)。
- [S13：.NET 单文件发布](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)。
- [S14：.NET 裁剪不兼容项中的 WPF](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)。
- [S15：LibreOffice 官方启动参数](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html)。
- [S16：LibreOffice 官方转换过滤器](https://help.libreoffice.org/latest/en-US/text/shared/guide/convertfilters.html)。
- [S17：LibreOffice 官方许可说明及 MPL 2.0 正文](https://www.libreoffice.org/about-us/licenses/)。
- [S18：Telerik 官方内容控件文档源文件](https://github.com/telerik/xaml-docs/blob/master/controls/radrichtextbox/features/content-controls/content-controls.md)。
- [S19：DevExpress WPF Rich Text Editor 产品页](https://www.devexpress.com/products/net/controls/wpf/rich_editor/)。实际获取的是产品页；`docs.devexpress.com/WPF/6136/controls-and-libraries/richedit` 的挑战页不是功能证据。
- [S20：Syncfusion WPF RichTextBox 概览](https://help.syncfusion.com/wpf/richtextbox/overview)。
- [S21：ONLYOFFICE Desktop Editors 官方 README](https://github.com/ONLYOFFICE/DesktopEditors/blob/master/README.md)。
- [S22：ONLYOFFICE Desktop Editors 官方 LICENSE](https://github.com/ONLYOFFICE/DesktopEditors/blob/master/LICENSE)。
