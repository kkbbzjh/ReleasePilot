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

## 自动构建

推送 `v*` 标签后执行 `.github/workflows/release.yml`，并发布 ReleasePilot.exe。也可在 Actions 手动运行工作流，只生成可下载构建附件。

## 后续规划

V0.2 支持自动使用 Windows 系统代理；V0.3 增加后台监控和通知。

> 部分软件的 EXE 文件版本与 GitHub 标签不同，无法安全比较时会显示“需要确认”。
