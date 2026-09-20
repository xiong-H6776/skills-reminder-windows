# Skills Reminder

一个独立、只读的 Windows 11 Skills 提醒组件。

## 功能

- 自动读取 `%USERPROFILE%\.codex\skills`，支持根目录 Junction。
- 单独识别普通 Skills 与 `.system` 系统 Skills。
- 中文白色无边框面板、窗口置顶、搜索、收藏和最近使用排序。
- 使用 Per-Monitor V2 DPI 感知，改善 Windows 缩放下的文字和控件清晰度。
- `Ctrl + Alt + Space` 全局显示或隐藏。
- 双击或按钮复制推荐调用语句；也可只复制 Skill 名称。
- 尝试停靠在标题包含 ChatGPT 或 Codex 的窗口旁，找不到时停靠主屏幕右侧。
- 托盘常驻；关闭窗口只隐藏，托盘菜单“退出”才完全结束。
- 开机启动默认关闭；启用前会明确确认并备份原注册表值。

## 数据边界

- Skill 文件始终只读，不会删除、停用、迁移或覆盖 Skill。
- 收藏和最近使用保存在 `D:\CodexData\SkillsReminder`。
- 不联网，不注入 ChatGPT/Codex，不依赖其内部接口。

## 构建

在 Windows PowerShell 中执行：

```powershell
& '.\build.ps1'
```

使用 Windows 自带的 .NET Framework 编译器，不下载第三方依赖。

## 卸载/回滚

先从托盘退出程序，再删除 `D:\Apps\SkillsReminder`。如不再需要收藏和最近记录，可另行备份后删除 `D:\CodexData\SkillsReminder`。如果曾启用开机启动，应先在界面中关闭；原值备份位于数据目录的 `startup-registry-backup.txt`。
