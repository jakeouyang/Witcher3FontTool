using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Witcher3FontTool;

public sealed class MainForm : Form
{
    static readonly Color Red = Color.FromArgb(235, 32, 39);
    readonly TextBox game = Input(), font = Input();
    readonly RichTextBox log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.Silver, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 10), DetectUrls = false };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(40, 200, 90), Anchor = AnchorStyles.Left };
    readonly Button language;
    readonly CheckBox simplified = CoverageCheck(), traditional = CoverageCheck(), english = CoverageCheck();
    readonly List<Control> busyControls = new();
    Label subtitle = null!, gameLabel = null!, fontLabel = null!, coverageLabel = null!, guidance = null!;
    Button install = null!, restore = null!;
    bool busy;
    bool interactionStarted;
    bool Chinese;
    FontCoverage Coverage => (simplified.Checked ? FontCoverage.SimplifiedChinese : 0) |
                             (traditional.Checked ? FontCoverage.TraditionalChinese : 0) |
                             (english.Checked ? FontCoverage.English : 0);

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

    public MainForm()
    {
        Text = "WITCHER 3 Font Tool"; BackColor = Color.Black; ForeColor = Red;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10.5f);
        ClientSize = new Size(940, 700); MinimumSize = new Size(940, 640); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        // Window icon intentionally left default in the open-source build.

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 12, 28, 12), ColumnCount = 1, RowCount = 10 };
        foreach (int h in new[] { 52, 38, 48, 48, 44, 42, 48, 8 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        var titleArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44)); titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = new Label { Text = "WITCHER 3 Font Tool", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 18), Margin = Padding.Empty };
        title.MouseDown += DragWindow;
        titleArea.Controls.Add(title, 1, 0); header.Controls.Add(titleArea, 0, 0);
        language = Button("", () => SelectInterfaceLanguage(!Chinese));
        language.Dock = DockStyle.Fill; language.Margin = Padding.Empty;
        header.Controls.Add(language, 1, 0);
        var link = Button("GitHub", () => Process.Start(new ProcessStartInfo("https://github.com/jakeouyang/Witcher3FontTool") { UseShellExecute = true }));
        link.Dock = DockStyle.Fill; link.Margin = new Padding(0, 2, 8, 2); header.Controls.Add(link, 2, 0);
        header.Controls.Add(Button("—", () => WindowState = FormWindowState.Minimized), 3, 0);
        header.Controls.Add(Button("×", Close), 4, 0); layout.Controls.Add(header, 0, 0);

        subtitle = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Silver };
        layout.Controls.Add(subtitle, 0, 1);

        gameLabel = new Label(); fontLabel = new Label();
        layout.Controls.Add(PathRow(gameLabel, game, true), 0, 2);
        layout.Controls.Add(PathRow(fontLabel, font, false), 0, 3);

        var coverageRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        coverageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        for (int i = 0; i < 3; i++) coverageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        simplified.Checked = true; english.Checked = true;
        coverageRow.Controls.Add(coverageLabel = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        coverageRow.Controls.Add(simplified, 1, 0); coverageRow.Controls.Add(traditional, 2, 0); coverageRow.Controls.Add(english, 3, 0);
        layout.Controls.Add(coverageRow, 0, 4);
        busyControls.AddRange(new Control[] { simplified, traditional, english, language });

        guidance = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray, Font = new Font("Microsoft YaHei UI", 9) };
        layout.Controls.Add(guidance, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
        install = Button("", () => Run(true)); restore = Button("", () => Run(false));
        install.Width = 200; restore.Width = 160; install.Height = restore.Height = 40;
        actions.Controls.Add(install); actions.Controls.Add(restore); layout.Controls.Add(actions, 0, 6);
        busyControls.AddRange(new Control[] { install, restore });

        var panel = new Panel { Dock = DockStyle.Fill, BackColor = log.BackColor, Padding = new Padding(12), Margin = new Padding(0, 6, 0, 6) };
        panel.Controls.Add(log); layout.Controls.Add(panel, 0, 8);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        status.Text = "Ready";
        footer.Controls.Add(status); footer.Controls.Add(new Label { Text = "v1.2", Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9) }); layout.Controls.Add(footer, 0, 9);

        busyControls.AddRange(new Control[] { game, font });
        string defaultGame = @"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3";
        if (BundleWriter.ValidateGameDir(defaultGame) == null) game.Text = defaultGame;
        ApplyLanguage();
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; status.Text = T("Working…", "正在处理…"); } };
    }

    void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
    }
    static CheckBox CoverageCheck() => new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.Silver, Margin = new Padding(4, 2, 2, 2) };
    static TextBox Input() => new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 6, 10, 0) };
    Button Button(string text, Action action)
    {
        var button = new Button { Text = text, Dock = DockStyle.None, ForeColor = Red, BackColor = Color.Black, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Height = 38, Width = 86, Margin = new Padding(0, 2, 12, 2), UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 12, 12); button.Click += (_, _) => action(); return button;
    }
    Control PathRow(Label label, TextBox box, bool directory)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; row.Controls.Add(label, 0, 0); row.Controls.Add(box, 1, 0);
        var browse = Button("", () =>
        {
            if (directory)
            {
                interactionStarted = true;
                using var dialog = new FolderBrowserDialog { Description = T("Select the game root folder", "选择巫师3游戏根目录"), UseDescriptionForTitle = true };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    box.Text = dialog.SelectedPath;
                    var error = BundleWriter.ValidateGameDir(box.Text);
                    Append(error ?? T("Game folder validated.", "游戏目录校验通过。"));
                }
            }
            else
            {
                interactionStarted = true;
                using var dialog = new OpenFileDialog { Filter = "Font files (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc", Title = T("Choose a font", "选择字体") };
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
            }
        });
        browse.Dock = DockStyle.Fill; browse.Margin = new Padding(0, 2, 0, 2); row.Controls.Add(browse, 2, 0);
        busyControls.AddRange(new Control[] { box, browse }); return row;
    }
    string T(string english, string chinese) => Chinese ? chinese : english;
    internal void SelectInterfaceLanguage(bool chinese) { Chinese = chinese; ApplyLanguage(); }
    void ApplyLanguage()
    {
        language.Text = T("中文", "English");
        if (!interactionStarted) status.Text = T("Ready", "就绪");
        subtitle.Text = T("Create a Witcher 3 font mod  /  Install and restore", "巫师3字体 MOD  /  生成、安装与还原");
        gameLabel.Text = T("Game path", "游戏目录"); fontLabel.Text = T("Font file", "字体文件");
        coverageLabel.Text = T("Languages", "替换语言");
        simplified.Text = T("Simplified Chinese", "简体中文");
        traditional.Text = T("Traditional Chinese", "繁體中文"); english.Text = T("English", "英文");
        guidance.Text = T("Select one or more game languages. Each selected language gets its own font library.", "可多选游戏语言；为每个选中语言生成独立字库，替换所有字体槽位。");
        install.Text = T("Build and install", "生成并安装"); restore.Text = T("Restore font", "还原字体");
        foreach (var row in new[] { game.Parent, font.Parent })
            if (row is TableLayoutPanel table && table.Controls.Count > 2 && table.Controls[2] is Button browse) browse.Text = T("Open", "浏览");
        if (!interactionStarted)
        {
            log.Clear();
            Append(T("Select the game folder and a TTF, OTF, or TTC font. The mod installs into Mods.", "选择游戏目录与 TTF、OTF 或 TTC 字体，生成后安装到 Mods。"));
            Append(T("Choose the languages used by your game. English is included by default.", "选择要替换的游戏语言，可多选；默认包含英文。"));
        }
    }
    void Append(string text) { log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}"); log.ScrollToCaret(); }
    async void Run(bool shouldInstall)
    {
        if (busy) return;
        string root = game.Text.Trim(), file = font.Text.Trim().Trim('"');
        if (root.Length == 0 || (shouldInstall && !File.Exists(file))) { Append(T("Choose a valid game folder and font file first.", "请先选择有效的游戏目录和字体文件。")); return; }
        if (shouldInstall && Coverage == 0) { Append(T("Select at least one font character set.", "请至少选择一种字体字符集。")); return; }
        interactionStarted = true;
        var selectedCoverage = Coverage; bool chinese = Chinese;
        busy = true; busyControls.ForEach(control => control.Enabled = false); status.Text = T("Working…", "正在处理…");
        try
        {
            var progress = new Progress<string>(Append);
            await Task.Run(() =>
            {
                Action<string> report = value => ((IProgress<string>)progress).Report(value);
                if (shouldInstall) FontService.Install(root, file, report, selectedCoverage, chinese);
                else FontService.Restore(root, report);
            });
            status.Text = T("Done", "完成");
        }
        catch (UnauthorizedAccessException) { status.Text = T("Access denied", "没有写入权限"); Append(T("Cannot write to the game folder. Close the tool and run it as administrator.", "无法写入游戏目录。请以管理员身份重新运行。")); }
        catch (Exception ex) { status.Text = T("Operation failed", "操作未完成"); Append(Chinese ? "操作失败：" + ex.Message : ex.Message); }
        finally { busy = false; busyControls.ForEach(control => control.Enabled = true); }
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var pen = new Pen(Red); e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1); }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x84 && (int)message.Result == 1)
        {
            var point = PointToClient(Cursor.Position);
            if (point.X >= Width - 12 && point.Y >= Height - 12) message.Result = (IntPtr)17;
        }
    }
}
