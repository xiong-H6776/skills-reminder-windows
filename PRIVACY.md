# Privacy

Skills Reminder 是本地运行的桌面工具。

## 读取的数据

- 默认读取 `%USERPROFILE%\.codex\skills` 下的目录和 `SKILL.md` 文件。
- 读取 Skill 的名称、ID、说明、目录位置及其用户/系统类型。
- 为窗口跟随功能读取顶层窗口的可见、前台和最小化状态，以及 ChatGPT/Codex 相关进程名称。

程序不会读取 ChatGPT/Codex 的聊天内容、账号信息、Cookie、网络流量或客户端内部数据。

## 保存的数据

当前测试版在 `D:\CodexData\SkillsReminder` 保存：

- 收藏列表。
- 最近复制过的 Skill 及时间顺序。
- 窗口跟随开关。
- 首次修改开机启动项之前的原注册表值备份。

这些数据仅保存在本机。正式 Release 前计划改用 `%LOCALAPPDATA%\SkillsReminder`，并提供可配置路径。

## 剪贴板和注册表

- 只有在用户点击复制按钮或双击 Skill 时，程序才会向剪贴板写入文本。
- 开机启动默认关闭。只有用户明确确认后，程序才会修改当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

## 网络与遥测

当前版本不包含网络请求、云同步、遥测、分析、广告或崩溃上报。

## 删除数据

先从托盘完全退出程序，再备份并删除 Skills Reminder 的数据目录。删除收藏和最近记录不会修改任何 Skill。

## 截图和问题反馈

提交 Issue 前，请自行检查截图、日志和路径中是否包含不希望公开的信息。
