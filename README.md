# ReleasePilot

Windows 桌面 GitHub 软件更新管理器。

## V0.1

- 添加 GitHub 仓库订阅
- 关联本地 EXE 并读取文件版本
- 获取最新正式版 Release 和更新日志
- 将订阅保存在本机 SQLite 中
- 在 GitHub Actions 上构建 Windows x64 单文件程序

## 开发

Windows + .NET 10 SDK：`dotnet restore`、`dotnet run`。

版本比较回归检查：`dotnet run --project tests/VersionHelper.Tests/VersionHelper.Tests.csproj -c Release`。Windows 下可追加 `-- <EXE 路径> <Release 标签>`，验证实际发布文件的版本。

版本比较支持 `v` 前缀、末尾补零（`0.2.0` 与 `0.2.0.0`）、构建元数据（`0.2.0+提交号`）以及预发布版本。无法解析的日期、提交号或自定义标签仍显示“需要确认”。当前仅检查版本，不会下载、解压或替换软件文件。

## 自动构建

推送 `v*` 标签后执行 `.github/workflows/release.yml`，并发布带版本号的 EXE（如 `ReleasePilot-v0.2.0.exe`）和 `SHA256SUMS.txt`，校验文件使用相同的 EXE 文件名。标签构建使用标签版本，`main` 分支和手动构建使用项目文件中的 `Version`。也可在 Actions 手动运行工作流，只生成可下载构建附件。

## 后续规划

V0.2 支持自动使用 Windows 系统代理；V0.3 增加后台监控和通知。

> 部分软件的 EXE 文件版本与 GitHub 标签不同，无法安全比较时会显示“需要确认”。
