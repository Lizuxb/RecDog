# RecDog · 听录

Windows 本地文档和录音工具，采用 WPF + WebView2 + canvas-editor。当前支持项目/人员资料管理、文档编辑和 DOCX 导入导出、模板管理及本地 MP3 录音；转写尚未接入。

## 打开软件

双击项目根目录的 `Start-RecDog.cmd`。当前构建程序在 `artifacts/app-recording-workflow/RecDog.exe`。本机脚本使用 `D:\DevTools\dotnet`，无需修改系统环境变量。

运行需要 .NET 10 Desktop Runtime 和 WebView2 Runtime；当前为依赖本机运行时的目录构建，尚未制作安装包。不能只复制 EXE，需保留整个程序目录。

点击左侧当前项目打开项目管理，列表显示本机创建或打开过的项目及路径。无项目时显示新增提示。新增项目默认按“项目01、项目02……”命名，在“文档/RecDog/Projects”下自动创建同名文件夹，也可选择其他父目录；打开已有项目仍可选择项目文件夹。可重命名、关闭或删除当前项目。点击人员名称展开或收起其下的文档与录音，收起不会关闭当前编辑页，其他人员仍可见。项目内先创建人员文件夹，再创建或打开具体文档，从文档底部开始录音；人员首页和模板编辑不能开始录音。自建模板可编辑、删除；内置模板保留。

旧版资料仍在 `src/RecDog.Desktop/bin/Release/net10.0-windows/data/workspace`，可通过“打开项目”选择该目录。程序兼容 schemaVersion 1；首次写入后升级 schemaVersion 2，原有项目级文档保留。新版默认数据位置是程序目录内的 `data/workspace`，也可使用自选项目目录。

## 录音与恢复

录音前可在底部选择 Windows 麦克风，并刷新设备列表；录音期间锁定设备并显示实际设备名。声音方格来自真实 PCM 输入，仅录音期间显示，暂停时熄灭，停止后隐藏；持续低音量或接近满幅时显示提示。左右栏边缘支持拖动调整宽度并记住设置，列表独立滚动。左侧按创建时间从早到晚并列展示文档/录音及时间，保存不改变新文档的位置。点击录音可在编辑页面回放，省略号菜单支持导出 MP3、打开所在文件夹、复制路径。开始后可暂停、继续、停止并保存；录音保存在开始时的人员下，新录音的界面名称和磁盘 MP3 名称保持一致，采用人员名、开始时文档名及时间命名，重名自动追加序号；已有录音文件不自动改名。不建立文档与录音的固定关联；可在编辑页面回放 MP3。关闭程序会等待保存。设备故障或编码失败时，提示中会给出恢复 WAV 的路径。

文档/录音/人员删除前在项目 `.trash` 保存原 workspace.json，媒体移入该目录；整个项目删除后移入同级 `.RecDog-trash`。恢复前请先备份当前项目，根据快照中的路径还原媒体，再恢复 workspace.json。删除项目不会清空回收目录。

## 开发与验证

执行 `scripts/build.ps1` 构建到 `artifacts/app-recording-workflow`，运行前端构建需要 Node 24、pnpm。更新前先关闭新版窗口；旧版位于独立目录，可继续保留。

依赖说明见 `THIRD_PARTY_NOTICES.md`。内部设计文档及本机验证产物不随源码上传。

首次开发：安装 .NET 10 SDK、Node 24、pnpm，执行 `pnpm --dir frontend install --frozen-lockfile`，再运行 `scripts/build.ps1`。

验证：

```powershell
dotnet run --project tests/RecDog.Core.Tests
dotnet run --project tests/RecDog.Audio.Tests
node --test frontend/tests/*.test.mjs
pnpm --dir frontend exec playwright install msedge
node frontend/tests/browser.mjs
```
