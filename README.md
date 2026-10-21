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

版本比较支持 `v` 前缀、末尾补零（`0.2.0` 与 `0.2.0.0`）、构建元数据（`0.2.0+提交号`）以及预发布版本。无法解析的日期、提交号或自定义标签显示“需要确认”，可在核实本地已安装最新 Release 后使用“确认已安装当前版”，也可取消确认。EXE 确认绑定文件内容，文件变化后会重新提示。

## 安装与更新

选择订阅后，点击“安装 / 更新”，选择 Release 附件与更新方式。每次执行前都会显示附件、版本和目标位置供核对：

- **EXE / MSI 安装包**：下载后打开正常安装向导。安装器退出后，用户确认安装成功并选择安装后的主程序，才记录版本；不会把下载完成或启动安装器当作安装成功。
- **ZIP / 7z 压缩包**：使用“关联目录”选择软件或配置包目录，先解压到临时目录，再备份并覆盖同名文件，不删除包中未包含的本地文件。可填写不覆盖的相对路径（分号分隔，如 `mpv.conf;input.conf;portable_config/mpv.conf`）；可选择去掉唯一顶层目录。配置包根据成功更新或手动确认的 Release 记录追踪版本，不使用播放器 EXE 版本，也不逐个监控配置文件变化。
- **单文件 EXE**：使用“关联 EXE”选择本地主程序，备份后替换。请先关闭目标程序；应用文件失败时回滚已修改的文件。ReleasePilot 自身由独立助手在退出后替换并重启。

例如 `dyphire/mpv-config` 的 7z 整合包，应选择整合包解压目录；单独配置包则选择实际配置目录，并根据包内目录结构决定是否去掉顶层目录。自定义配置应加入保留列表。

下载与旧文件备份保留在 `%LOCALAPPDATA%/ReleasePilot/Updates/`。附件提供 GitHub SHA256 摘要时会核验，大小或摘要不匹配时停止更新。旧订阅数据库会自动增加所需字段，保留原有订阅。

## 自动构建

推送 `v*` 标签后执行 `.github/workflows/release.yml`，并发布带版本号的 EXE（如 `ReleasePilot-v0.2.0.exe`）和 `SHA256SUMS.txt`，校验文件使用相同的 EXE 文件名。标签构建使用标签版本，`main` 分支和手动构建使用项目文件中的 `Version`。也可在 Actions 手动运行工作流，只生成可下载构建附件。

## 后续规划

V0.2 支持自动使用 Windows 系统代理；V0.3 增加后台监控和通知。

> 部分软件的 EXE 文件版本与 GitHub 标签不同，无法安全比较时会显示“需要确认”。
