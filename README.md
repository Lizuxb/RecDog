# RecDog · 听录

面向访谈、会议和模板化记录的单人 Windows 桌面工具。目标流程是：创建项目 → 选择模板 → 录音与转写 → 编辑文档 → 确认正式版本 → 导出 DOCX。单机优先，不强制登录；AI 整理是可选处理。

## 当前状态

**规划与工程初始化阶段，尚不可用于正式录音或记录。**

- 已有需求、产品设计、技术方案、ADR 和静态参考原型。
- 已建立 WPF/.NET 工程骨架；窗口仅用于启动检查，无业务功能。
- 尚未完成编译、真实模板、四小时录音、中文转写或发行验收。
- 首先开展风险验证 MVP；验证通过后逐阶段实现完整 V1，不把原型演示当成产品交付。

## 开发与运行

前提：Windows、Git、支持 WPF 的 .NET 10 SDK。仅安装 .NET 运行时不能构建项目。

```powershell
dotnet --info
dotnet restore RecDog.slnx
dotnet build RecDog.slnx -c Release --no-restore
dotnet run --project src/RecDog.Desktop/RecDog.Desktop.csproj
```

当前初始化环境只有 .NET 6 运行时，没有 SDK，故上述构建与启动尚未实测。目标 Windows 版本支持和最终 SDK 补丁版本需在 MVP 阶段验证并锁定；不承诺所有 Windows 10 版本兼容。

测试目录暂为职责说明，尚未建立测试项目；不应将 `dotnet test` 无用例视作通过。无需安装 Word/WPS 的目标也尚未验收。

## 技术方向

候选方案为 C# / WPF / MVVM、SQLite、NAudio/WASAPI、FFmpeg、sherpa-onnx、Open XML SDK。本次骨架只使用 .NET/WPF 标准组件；其余依赖待实测和许可证核对后固定版本。

这是桌面应用，不部署 Web 后端。Desktop 负责交互和应用协调，Core 负责领域规则，Infrastructure 负责设备、文件、存储和服务适配。录音采集、本机转写按计划由独立进程隔离。

## 目录与文档入口

| 目录或文件 | 用途 |
| --- | --- |
| [产品开发计划](docs/development-plan.md) | 阶段、实施内容、验收门槛与协作策略 |
| [MVP 验证清单](docs/validation/mvp-checklist.md) | 风险验证步骤、证据和停止条件 |
| [需求规格](docs/requirements-v1.md) | 已确定的产品行为与范围 |
| [产品设计](docs/product-design-v1.md) | 功能位置、主流程与异常交互 |
| [技术方案](docs/technical-design-v1.md) | 候选架构、数据与技术验证事项 |
| [术语表](GLOSSARY.md) | 产品概念与统一名称 |
| [docs/adr](docs/adr/) | 架构决策记录 |
| [docs/research](docs/research/) | 调研依据和候选比较 |
| [prototype](prototype/README.md) | 旧静态参考原型，与正式代码分开 |
| [src](src/README.md) | 正式应用工程骨架 |
| [tests](tests/) | 领域与集成测试预留目录 |
| [.github](.github/README.md) | 后续 GitHub CI 配置位置 |

## 数据与版本管理

代码、需求、设计与验证结论一起提交。真实录音、个人项目、模型、密钥、日志和构建产物不入库；默认忽略规则见 [.gitignore](.gitignore)。真实模板使用前需脱敏，验证原始数据保存在仓库外。

当前只建立本地 `main`，不配置远程。原 `Record Note` 保留为历史资料目录，后续开发以本仓库文档为准；其中旧文档的“本次交付”等语句保留其原始日期语境。

尚未选择开源许可证，也未发布到 GitHub；仓库初始化不代表已授权公开分发。
