# 源代码边界

- `RecDog.Desktop`：WPF 视图、视图模型、命令与应用组装入口。
- `RecDog.Core`：项目、文档、录音目标、来源与版本规则；不依赖 WPF、SQLite 或网络实现。
- `RecDog.Infrastructure`：存储、设备、文件及服务适配；引用 Core，不引用 Desktop。

当前只有可供构建验证的工程骨架，尚无业务实现。AudioHost 与 AsrWorker 在相应 MVP 验证时再建立独立进程工程。不要先创建无实现的业务接口或引入所有候选依赖。
