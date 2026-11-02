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

## 目录

| 目录 | 用途 |
| --- | --- |
| [prototype](prototype/) | 静态参考原型 |
| [src](src/) | 正式应用工程骨架 |
| `tests/`（本地预留） | 测试目录，尚未建立测试项目 |

## 数据与版本管理

仓库跟踪源代码、工程配置和本 README。内部需求、设计、调研、开发计划及其他文档保留在本地，不纳入 Git 跟踪。真实录音、个人项目、模型、密钥、日志和构建产物同样不入库；默认忽略规则见 [.gitignore](.gitignore)。真实模板使用前需脱敏。

GitHub 仓库：https://github.com/Lizuxb/RecDog

## 许可证

尚未选择开源许可证；公开代码不等于授予开源使用许可。
