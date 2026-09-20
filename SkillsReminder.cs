using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SkillsReminder
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            TryEnablePerMonitorDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReminderForm());
        }

        private static void TryEnablePerMonitorDpi()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch (EntryPointNotFoundException)
            {
                SetProcessDPIAware();
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }

    internal sealed class SkillInfo
    {
        public string Id;
        public string Name;
        public string Description;
        public string DirectoryPath;
        public bool IsSystem;
        public string Key { get { return (IsSystem ? "system|" : "user|") + Id; } }
    }

    internal sealed class ReminderForm : Form
    {
        private const int HotKeyId = 0x534B;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const int WmHotKey = 0x0312;
        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HitCaption = 0x0002;
        private const int ClassDropShadow = 0x00020000;
        private const string RunValueName = "SkillsReminder";

        private readonly string skillsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "skills");
        private readonly string dataRoot = @"D:\CodexData\SkillsReminder";
        private readonly string favoriteFile;
        private readonly string recentFile;
        private readonly int currentProcessId = Process.GetCurrentProcess().Id;

        private readonly TextBox searchBox = new TextBox();
        private readonly ListView skillList = new ListView();
        private readonly Label countLabel = new Label();
        private readonly Label hotKeyLabel = new Label();
        private readonly Label descriptionLabel = new Label();
        private readonly Label pathLabel = new Label();
        private readonly CheckBox topMostCheck = new CheckBox();
        private readonly CheckBox startupCheck = new CheckBox();
        private readonly Button favoriteButton = new Button();
        private readonly Button copyNameButton = new Button();
        private readonly Button copyPromptButton = new Button();
        private readonly Button refreshButton = new Button();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly Timer clientMonitor = new Timer { Interval = 600 };

        private readonly List<SkillInfo> allSkills = new List<SkillInfo>();
        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> recent = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private bool exiting;
        private bool initializing = true;
        private bool manuallyHidden;
        private bool manualShowOverride;
        private bool hiddenForClient;
        private IntPtr followedClient = IntPtr.Zero;
        private NativeRect lastClientRect;
        private bool hasLastClientRect;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle &= ~ClassDropShadow;
                return parameters;
            }
        }

        public ReminderForm()
        {
            favoriteFile = Path.Combine(dataRoot, "favorites.txt");
            recentFile = Path.Combine(dataRoot, "recent.tsv");
            BuildUi();
            Directory.CreateDirectory(dataRoot);
            LoadState();
            ScanSkills();
            RefreshList();
            SetupTray();
            followedClient = FindClientWindow();
            PositionNearClient(followedClient);
            clientMonitor.Tick += MonitorClientWindow;
            clientMonitor.Start();
            initializing = false;
        }

        private void BuildUi()
        {
            Text = "Skills 提醒";
            Width = 430;
            Height = 640;
            MinimumSize = new Size(390, 520);
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(203, 213, 225);
            Padding = new Padding(1);
            ForeColor = Color.FromArgb(32, 38, 46);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowIcon = false;
            DoubleBuffered = true;
            TopMost = true;
            ShowInTaskbar = true;

            var titleBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.White };
            var caption = new Label
            {
                Text = "Skills 提醒",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                Location = new Point(14, 12)
            };
            var closeButton = CreateCaptionButton("×");
            var minimizeButton = CreateCaptionButton("—");
            closeButton.Dock = DockStyle.Right;
            minimizeButton.Dock = DockStyle.Right;
            closeButton.Click += delegate { HideManually(); };
            minimizeButton.Click += delegate { HideManually(); };
            titleBar.MouseDown += DragWindow;
            caption.MouseDown += DragWindow;
            titleBar.Controls.Add(caption);
            titleBar.Controls.Add(minimizeButton);
            titleBar.Controls.Add(closeButton);

            var header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 12, 14, 8), BackColor = Color.White };
            var title = new Label { Text = "Skills 提醒", AutoSize = true, Font = new Font(Font.FontFamily, 13F, FontStyle.Bold), Location = new Point(14, 13) };
            title.Margin = new Padding(0);
            countLabel.AutoSize = true;
            countLabel.ForeColor = Color.FromArgb(100, 110, 122);
            countLabel.Margin = new Padding(10, 4, 0, 0);
            var summaryRow = new FlowLayoutPanel
            {
                Location = new Point(14, 13),
                Size = new Size(390, 31),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            summaryRow.Controls.Add(title);
            summaryRow.Controls.Add(countLabel);
            searchBox.AccessibleName = "搜索名称或说明";
            searchBox.Location = new Point(14, 50);
            searchBox.Width = 300;
            searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            searchBox.TextChanged += delegate { RefreshList(); };
            refreshButton.Text = "刷新";
            refreshButton.Width = 72;
            refreshButton.Height = 27;
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refreshButton.Location = new Point(332, 49);
            refreshButton.Click += delegate { ScanSkills(); RefreshList(); };
            header.Controls.Add(summaryRow);
            header.Controls.Add(searchBox);
            header.Controls.Add(refreshButton);

            skillList.Dock = DockStyle.Fill;
            skillList.View = View.Details;
            skillList.FullRowSelect = true;
            skillList.HideSelection = false;
            skillList.MultiSelect = false;
            skillList.BorderStyle = BorderStyle.FixedSingle;
            skillList.Columns.Add("", 34);
            skillList.Columns.Add("Skill", 260);
            skillList.Columns.Add("类型", 70);
            skillList.SelectedIndexChanged += delegate { UpdateDetails(); };
            skillList.DoubleClick += delegate { CopyPrompt(); };

            var detail = new Panel { Dock = DockStyle.Bottom, Height = 196, Padding = new Padding(14, 10, 14, 10), BackColor = Color.FromArgb(248, 250, 252) };
            descriptionLabel.AutoEllipsis = true;
            descriptionLabel.Location = new Point(14, 10);
            descriptionLabel.Size = new Size(390, 45);
            descriptionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            descriptionLabel.ForeColor = Color.FromArgb(55, 65, 81);
            pathLabel.AutoEllipsis = true;
            pathLabel.Location = new Point(14, 59);
            pathLabel.Size = new Size(390, 22);
            pathLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            pathLabel.ForeColor = Color.FromArgb(107, 114, 128);

            favoriteButton.Text = "收藏";
            favoriteButton.Location = new Point(14, 88);
            favoriteButton.Size = new Size(72, 30);
            favoriteButton.Click += delegate { ToggleFavorite(); };
            copyNameButton.Text = "复制名称";
            copyNameButton.Location = new Point(94, 88);
            copyNameButton.Size = new Size(90, 30);
            copyNameButton.Click += delegate { CopyName(); };
            copyPromptButton.Text = "复制调用语句";
            copyPromptButton.Location = new Point(192, 88);
            copyPromptButton.Size = new Size(112, 30);
            copyPromptButton.Click += delegate { CopyPrompt(); };

            topMostCheck.Text = "窗口置顶";
            topMostCheck.Checked = true;
            topMostCheck.AutoSize = true;
            topMostCheck.Location = new Point(14, 130);
            topMostCheck.CheckedChanged += delegate { TopMost = topMostCheck.Checked; };
            startupCheck.Text = "开机启动（默认关闭）";
            startupCheck.AutoSize = true;
            startupCheck.Location = new Point(112, 130);
            startupCheck.CheckedChanged += StartupChanged;
            hotKeyLabel.Text = "快捷键：Ctrl + Alt + Space";
            hotKeyLabel.AutoSize = true;
            hotKeyLabel.Location = new Point(14, 160);
            hotKeyLabel.ForeColor = Color.FromArgb(100, 110, 122);

            detail.Controls.Add(descriptionLabel);
            detail.Controls.Add(pathLabel);
            detail.Controls.Add(favoriteButton);
            detail.Controls.Add(copyNameButton);
            detail.Controls.Add(copyPromptButton);
            detail.Controls.Add(topMostCheck);
            detail.Controls.Add(startupCheck);
            detail.Controls.Add(hotKeyLabel);

            Controls.Add(skillList);
            Controls.Add(detail);
            Controls.Add(header);
            Controls.Add(titleBar);
            FormClosing += OnFormClosing;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized) HideManually();
            };
        }

        private Button CreateCaptionButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Width = 46,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(55, 65, 81),
                TabStop = false,
                Font = new Font("Segoe UI Symbol", 10F, FontStyle.Regular)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 244, 246);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(229, 231, 235);
            return button;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNcLeftButtonDown, HitCaption, 0);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool ok = RegisterHotKey(Handle, HotKeyId, ModControl | ModAlt, (uint)Keys.Space);
            hotKeyLabel.Text = ok ? "快捷键：Ctrl + Alt + Space" : "快捷键注册失败（可能已被占用）";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, HotKeyId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotKey && m.WParam.ToInt32() == HotKeyId)
            {
                ToggleVisibility();
                return;
            }
            base.WndProc(ref m);
        }

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏", null, delegate { ToggleVisibility(); });
            menu.Items.Add("刷新 Skills", null, delegate { ScanSkills(); RefreshList(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { exiting = true; trayIcon.Visible = false; Close(); });
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Text = "Skills 提醒";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { ShowPanel(); };
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideManually();
            }
            else
            {
                clientMonitor.Stop();
                clientMonitor.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }

        private void ToggleVisibility()
        {
            if (Visible && WindowState != FormWindowState.Minimized) HideManually();
            else ShowPanel();
        }

        private void ShowPanel()
        {
            manuallyHidden = false;
            manualShowOverride = true;
            hiddenForClient = false;
            followedClient = FindClientWindow();
            PositionNearClient(followedClient);
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            searchBox.Focus();
        }

        private void HideManually()
        {
            manuallyHidden = true;
            manualShowOverride = false;
            Hide();
        }

        private void MonitorClientWindow(object sender, EventArgs e)
        {
            IntPtr foreground = GetForegroundWindow();
            bool foregroundIsClient = IsClientWindow(foreground);
            bool foregroundIsReminder = IsOwnProcessWindow(foreground);
            IntPtr target = foregroundIsClient ? foreground : FindClientWindow();
            bool coveredByAnotherApp = !foregroundIsClient && !foregroundIsReminder;

            if (target == IntPtr.Zero || IsIconic(target) || coveredByAnotherApp)
            {
                followedClient = target;
                hasLastClientRect = false;
                if (!manualShowOverride)
                {
                    hiddenForClient = true;
                    if (Visible) Hide();
                }
                return;
            }

            manualShowOverride = false;
            NativeRect currentRect;
            bool rectAvailable = GetWindowRect(target, out currentRect);
            bool clientChanged = target != followedClient;
            bool rectChanged = rectAvailable && (!hasLastClientRect || !SameRect(currentRect, lastClientRect));
            followedClient = target;

            if (clientChanged || rectChanged || hiddenForClient)
                PositionNearClient(target);

            if (rectAvailable)
            {
                lastClientRect = currentRect;
                hasLastClientRect = true;
            }

            if (hiddenForClient)
            {
                hiddenForClient = false;
                if (!manuallyHidden)
                {
                    WindowState = FormWindowState.Normal;
                    Show();
                    SetForegroundWindow(target);
                }
            }
        }

        private static bool SameRect(NativeRect left, NativeRect right)
        {
            return left.Left == right.Left && left.Top == right.Top &&
                   left.Right == right.Right && left.Bottom == right.Bottom;
        }

        private void ScanSkills()
        {
            allSkills.Clear();
            if (!Directory.Exists(skillsRoot))
            {
                countLabel.Text = "未找到 Skills 目录";
                return;
            }

            try
            {
                foreach (string directory in Directory.GetDirectories(skillsRoot))
                {
                    string id = Path.GetFileName(directory);
                    if (string.Equals(id, ".system", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string systemDirectory in Directory.GetDirectories(directory))
                            AddSkill(systemDirectory, true);
                    }
                    else
                    {
                        AddSkill(directory, false);
                    }
                }
            }
            catch (Exception ex)
            {
                countLabel.Text = "扫描失败：" + ex.Message;
            }
        }

        private void AddSkill(string directory, bool isSystem)
        {
            string skillMd = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(skillMd)) return;
            string id = Path.GetFileName(directory);
            string name = id;
            string description = "暂无说明";
            try
            {
                ParseFrontMatter(skillMd, ref name, ref description);
            }
            catch
            {
                description = "SKILL.md 可见，但说明解析失败";
            }
            allSkills.Add(new SkillInfo
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Description = string.IsNullOrWhiteSpace(description) ? "暂无说明" : description,
                DirectoryPath = directory,
                IsSystem = isSystem
            });
        }

        private static void ParseFrontMatter(string file, ref string name, ref string description)
        {
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length == 0 || lines[0].Trim() != "---") return;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim() == "---") break;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key == "name") name = Unquote(value);
                if (key == "description")
                {
                    if (value == ">" || value == "|")
                    {
                        var parts = new List<string>();
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            if (lines[j].Trim() == "---") break;
                            if (lines[j].Length > 0 && !char.IsWhiteSpace(lines[j][0])) break;
                            string part = lines[j].Trim();
                            if (part.Length > 0) parts.Add(part);
                            i = j;
                        }
                        description = string.Join(" ", parts.ToArray());
                    }
                    else description = Unquote(value);
                }
            }
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
                return value.Substring(1, value.Length - 2);
            return value;
        }

        private void RefreshList()
        {
            string query = searchBox.Text.Trim();
            IEnumerable<SkillInfo> filtered = allSkills;
            if (query.Length > 0)
            {
                filtered = filtered.Where(s =>
                    s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            filtered = filtered
                .OrderByDescending(s => favorites.Contains(s.Key))
                .ThenByDescending(s => recent.ContainsKey(s.Key) ? recent[s.Key] : 0)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase);

            skillList.BeginUpdate();
            skillList.Items.Clear();
            foreach (SkillInfo skill in filtered)
            {
                var item = new ListViewItem(favorites.Contains(skill.Key) ? "\u2605" : "\u2606");
                item.SubItems.Add(skill.Name);
                item.SubItems.Add(skill.IsSystem ? "系统" : "用户");
                item.Tag = skill;
                if (skill.IsSystem) item.ForeColor = Color.FromArgb(99, 102, 241);
                skillList.Items.Add(item);
            }
            skillList.EndUpdate();
            int systemCount = allSkills.Count(s => s.IsSystem);
            int userCount = allSkills.Count - systemCount;
            countLabel.Text = string.Format("{0} 项 · 用户 {1} / 系统 {2}", skillList.Items.Count, userCount, systemCount);
            UpdateDetails();
        }

        private SkillInfo SelectedSkill()
        {
            return skillList.SelectedItems.Count == 1 ? skillList.SelectedItems[0].Tag as SkillInfo : null;
        }

        private void UpdateDetails()
        {
            SkillInfo skill = SelectedSkill();
            bool selected = skill != null;
            favoriteButton.Enabled = selected;
            copyNameButton.Enabled = selected;
            copyPromptButton.Enabled = selected;
            if (!selected)
            {
                descriptionLabel.Text = "选择一个 Skill 查看说明";
                pathLabel.Text = skillsRoot;
                favoriteButton.Text = "收藏";
                return;
            }
            descriptionLabel.Text = skill.Description;
            pathLabel.Text = skill.DirectoryPath;
            favoriteButton.Text = favorites.Contains(skill.Key) ? "取消收藏" : "收藏";
        }

        private void ToggleFavorite()
        {
            SkillInfo skill = SelectedSkill();
            if (skill == null) return;
            if (!favorites.Add(skill.Key)) favorites.Remove(skill.Key);
            SaveFavorites();
            RefreshList();
        }

        private void CopyName()
        {
            SkillInfo skill = SelectedSkill();
            if (skill == null) return;
            Clipboard.SetText(skill.Id);
            MarkRecent(skill);
        }

        private void CopyPrompt()
        {
            SkillInfo skill = SelectedSkill();
            if (skill == null) return;
            Clipboard.SetText("请使用 $" + skill.Id + " 帮我完成：");
            MarkRecent(skill);
        }

        private void MarkRecent(SkillInfo skill)
        {
            recent[skill.Key] = DateTime.UtcNow.Ticks;
            SaveRecent();
            Text = "Skills 提醒 — 已复制 " + skill.Id;
            var timer = new Timer { Interval = 1400 };
            timer.Tick += delegate { Text = "Skills 提醒"; timer.Stop(); timer.Dispose(); };
            timer.Start();
        }

        private void LoadState()
        {
            if (File.Exists(favoriteFile))
                foreach (string line in File.ReadAllLines(favoriteFile, Encoding.UTF8))
                    if (!string.IsNullOrWhiteSpace(line)) favorites.Add(line.Trim());

            if (File.Exists(recentFile))
            {
                foreach (string line in File.ReadAllLines(recentFile, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    long ticks;
                    if (parts.Length == 2 && long.TryParse(parts[1], out ticks)) recent[parts[0]] = ticks;
                }
            }
            startupCheck.Checked = IsStartupEnabled();
        }

        private void SaveFavorites()
        {
            File.WriteAllLines(favoriteFile, favorites.OrderBy(x => x).ToArray(), new UTF8Encoding(false));
        }

        private void SaveRecent()
        {
            string[] lines = recent.OrderByDescending(x => x.Value).Take(50).Select(x => x.Key + "\t" + x.Value).ToArray();
            File.WriteAllLines(recentFile, lines, new UTF8Encoding(false));
        }

        private bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                string value = key == null ? null : key.GetValue(RunValueName) as string;
                return !string.IsNullOrEmpty(value) && value.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private void StartupChanged(object sender, EventArgs e)
        {
            if (initializing) return;
            bool enable = startupCheck.Checked;
            string action = enable ? "启用" : "关闭";
            DialogResult result = MessageBox.Show(
                action + "开机启动会修改当前用户的 Run 注册表项。\n\n继续吗？",
                "确认开机启动设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                initializing = true;
                startupCheck.Checked = !enable;
                initializing = false;
                return;
            }

            string backup = Path.Combine(dataRoot, "startup-registry-backup.txt");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                object previous = key.GetValue(RunValueName);
                if (!File.Exists(backup))
                    File.WriteAllText(backup, DateTime.UtcNow.ToString("o") + "\r\n" + (previous == null ? "<missing>" : previous.ToString()), Encoding.UTF8);
                if (enable) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                else if (previous != null && previous.ToString().IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0)
                    key.DeleteValue(RunValueName, false);
            }
        }

        private void PositionNearClient()
        {
            PositionNearClient(FindClientWindow());
        }

        private void PositionNearClient(IntPtr target)
        {
            const int rightMargin = 30;
            const int bottomMargin = 38;
            Rectangle work = target != IntPtr.Zero ? Screen.FromHandle(target).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int x;
            int y;
            if (target != IntPtr.Zero)
            {
                NativeRect rect;
                GetWindowRect(target, out rect);
                int anchorRight = Math.Min(rect.Right, work.Right);
                int anchorBottom = Math.Min(rect.Bottom, work.Bottom);
                x = anchorRight - Width - rightMargin;
                y = anchorBottom - Height - bottomMargin;
            }
            else
            {
                x = work.Right - Width - rightMargin;
                y = work.Bottom - Height - bottomMargin;
            }
            x = Math.Max(work.Left, Math.Min(x, work.Right - Width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
            Location = new Point(x, y);
        }

        private IntPtr FindClientWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsClientWindow(foreground)) return foreground;
            if (IsClientWindow(followedClient)) return followedClient;

            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (IsClientWindow(handle))
                {
                    found = handle;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private bool IsClientWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero || handle == Handle || !IsWindow(handle) || !IsWindowVisible(handle))
                return false;

            int processId;
            GetWindowThreadProcessId(handle, out processId);
            if (processId == currentProcessId) return false;

            try
            {
                string processName = Process.GetProcessById(processId).ProcessName;
                if (processName.IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    processName.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                    return false;

                int length = GetWindowTextLength(handle);
                if (length <= 0) return false;
                var title = new StringBuilder(length + 1);
                GetWindowText(handle, title, title.Capacity);
                string value = title.ToString().Trim();
                return string.Equals(value, "ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "Codex", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsOwnProcessWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !IsWindow(handle)) return false;
            int processId;
            GetWindowThreadProcessId(handle, out processId);
            return processId == currentProcessId;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, int wParam, int lParam);
    }
}
