using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("모아 설치")]
[assembly: AssemblyProduct("모아")]
[assembly: AssemblyFileVersion(MoaSetup.Version)]
[assembly: AssemblyVersion(MoaSetup.Version)]

// 모아 설치·제거 프로그램. 관리자 권한 없이 사용자 폴더에 설치한다.
internal sealed class Options
{
    public bool Silent, Uninstall, RemoveData, NoDesktop;
    public string Dir, ShellRoot, RegKey = "Moa";

    public string ProgramsFolder { get { return ShellRoot != null ? Path.Combine(ShellRoot, "Programs") : Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
    public string DesktopFolder { get { return ShellRoot != null ? Path.Combine(ShellRoot, "Desktop") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
    public string StartShortcut { get { return Path.Combine(ProgramsFolder, "모아.lnk"); } }
    public string DesktopShortcut { get { return Path.Combine(DesktopFolder, "Moa.lnk"); } }
    public string RegPath { get { return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + RegKey; } }

    public static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Moa"); } }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--silent": o.Silent = true; break;
                case "--uninstall": o.Uninstall = true; break;
                case "--remove-data": o.RemoveData = true; break;
                case "--no-desktop": o.NoDesktop = true; break;
                case "--dir": o.Dir = args[++i]; break;
                case "--shell-root": o.ShellRoot = args[++i]; break;
                case "--reg-key": o.RegKey = args[++i]; break;
            }
        }
        return o;
    }
}

internal static class MoaSetup
{
    // 버전은 여기 한 곳만 고친다. 빌드 스크립트가 이 값으로 파일 이름을 만든다.
    internal const string Version = "1.2.2";

    [STAThread]
    private static int Main(string[] args)
    {
        Options options = Options.Parse(args);
#if UNINSTALLER
        if (args.Length == 1 && args[0] == "--self-test") return 0;
#else
        if (args.Length == 1 && args[0] == "--self-test") return ReadResource("Moa.exe").Length > 0 && ReadResource("uninstall.exe").Length > 0 && ReadResource("moa-logo.png").Length > 0 ? 0 : 2;
#endif
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
#if UNINSTALLER
            return Uninstall(options);
#else
            if (options.Silent) { Install(options, options.Dir ?? Options.DefaultDir, !options.NoDesktop, null); return 0; }
            Application.Run(new SetupWindow(options));
            return 0;
#endif
        }
        catch (Exception error)
        {
            if (!options.Silent) MessageBox.Show(error.Message, "모아 설치", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else Console.Error.WriteLine(error);
            return 1;
        }
    }

#if !UNINSTALLER
    internal static byte[] ReadResource(string name)
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var buffer = new MemoryStream())
        {
            if (source == null) throw new InvalidOperationException("설치 파일이 손상되었습니다: " + name);
            source.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

#endif

    private static string ShortcutTarget(string path)
    {
        if (!File.Exists(path)) return null;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        return shell.CreateShortcut(path).TargetPath;
    }

#if !UNINSTALLER
    private static void MakeShortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = Path.GetDirectoryName(target);
        link.IconLocation = target + ",0";
        link.Description = "모아 캔버스";
        link.Save();
    }

#endif

    private static string Normal(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
    }

#if !UNINSTALLER
    // 바탕화면의 기존 Moa 바로가기가 가리키는 곳에 작업 데이터가 있으면 그 폴더를 돌려준다.
    internal static string FindOldData(Options options, string dir)
    {
        string target = ShortcutTarget(options.DesktopShortcut);
        if (String.IsNullOrEmpty(target) || !File.Exists(target)) return null;
        string folder = Path.GetDirectoryName(target), data = Path.Combine(folder, "MoaData");
        if (Normal(folder) == Normal(dir) || !Directory.Exists(data)) return null;
        return Directory.Exists(Path.Combine(data, "App")) || Directory.Exists(Path.Combine(data, "Browser")) ? data : null;
    }

#endif

    private static void ThrowIfRunning(params string[] folders)
    {
        foreach (Process process in Process.GetProcessesByName("Moa"))
        {
            string path;
            try { path = process.MainModule.FileName; } catch (Exception) { continue; }
            if (folders.Any(folder => folder != null && Normal(path).StartsWith(Normal(folder) + Path.DirectorySeparatorChar)))
                throw new InvalidOperationException("모아가 실행 중입니다. 모아 창을 닫은 뒤 다시 시도해 주세요.");
        }
    }

#if !UNINSTALLER
    internal static void Install(Options options, string dir, bool desktop, Action<string> report)
    {
        Action<string> say = report ?? (text => { });
        string oldData = FindOldData(options, dir), data = Path.Combine(dir, "MoaData");
        bool import = oldData != null && !Directory.Exists(data);
        ThrowIfRunning(dir, import ? Path.GetDirectoryName(oldData) : null);

        say("파일 복사 중");
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "Moa.exe"), temp = exe + ".new";
        File.WriteAllBytes(temp, ReadResource("Moa.exe"));
        if (File.Exists(exe)) File.Delete(exe);
        File.Move(temp, exe);
        string uninstaller = Path.Combine(dir, "uninstall.exe");
        File.WriteAllBytes(uninstaller, ReadResource("uninstall.exe"));

        if (import)
        {
            say("기존 작업 가져오는 중");
            string partial = data + ".tmp";
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            // 새 프로필이 이미 있으면 예전 Edge 프로필은 다시 쓰이지 않으므로 복사하지 않는다.
            bool skipBrowser = Directory.Exists(Path.Combine(oldData, "App", "EBWebView"));
            CopyFolder(oldData, partial, skipBrowser ? "Browser" : null);
            Directory.Move(partial, data);
        }

        say("바로가기 만드는 중");
        MakeShortcut(options.StartShortcut, exe);
        if (desktop) MakeShortcut(options.DesktopShortcut, exe);

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(options.RegPath))
        {
            key.SetValue("DisplayName", "모아");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "모아");
            key.SetValue("DisplayIcon", exe + ",0");
            key.SetValue("InstallLocation", dir);
            key.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall --dir \"" + dir + "\" --reg-key \"" + options.RegKey + "\"" + (options.ShellRoot != null ? " --shell-root \"" + options.ShellRoot + "\"" : ""));
            key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    private static void CopyFolder(string from, string to, string skip)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (string folder in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(folder);
            if (!String.Equals(name, skip, StringComparison.OrdinalIgnoreCase)) CopyFolder(folder, Path.Combine(to, name), null);
        }
    }

#else
    private static int Uninstall(Options options)
    {
        string dir = options.Dir ?? Options.DefaultDir;
        bool removeData = options.RemoveData;
        if (!options.Silent)
        {
            if (MessageBox.Show("모아를 제거할까요?", "모아 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            string data = Path.Combine(dir, "MoaData");
            if (Directory.Exists(data))
                removeData = MessageBox.Show("작업 데이터도 함께 지울까요?\n\n아니요를 누르면 보드, 첨부 파일, 백업이 아래 폴더에 남습니다.\n" + data + "\n\n지우면 되돌릴 수 없습니다.", "모아 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        ThrowIfRunning(dir);

        string exe = Path.Combine(dir, "Moa.exe");
        foreach (string link in new[] { options.StartShortcut, options.DesktopShortcut })
        {
            string target = ShortcutTarget(link);
            if (target != null && Normal(target) == Normal(exe)) File.Delete(link);
        }
        Registry.CurrentUser.DeleteSubKeyTree(options.RegPath, false);
        if (File.Exists(exe)) File.Delete(exe);
        if (removeData && Directory.Exists(Path.Combine(dir, "MoaData"))) Directory.Delete(Path.Combine(dir, "MoaData"), true);

        // 자기 자신을 지우는 동작은 보안 프로그램에 막힐 수 있어 하지 않는다. 다음 설치 때 덮어쓴다.
        if (!options.Silent)
            MessageBox.Show("모아를 제거했습니다." + (removeData ? "" : "\n작업 데이터는 남겨 두었습니다.") + "\n\n남은 파일 위치: " + dir, "모아 제거", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
#endif
}

#if !UNINSTALLER
internal sealed class SetupWindow : Form
{
    private readonly Options options;
    private readonly TextBox folder = new TextBox();
    private readonly Button browse = new Button { Text = "찾아보기", AutoSize = true };
    private readonly CheckBox desktop = new CheckBox { Text = "바탕화면에 바로가기 만들기", Checked = true, AutoSize = true };
    private readonly Label note = new Label { AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Color.FromArgb(101, 92, 115) };
    private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Button action = new Button { Text = "설치", AutoSize = true, MinimumSize = new Size(96, 32) };
    private readonly Button cancel = new Button { Text = "취소", AutoSize = true, MinimumSize = new Size(96, 32) };
    private bool installed;

    public SetupWindow(Options options)
    {
        this.options = options;
        Text = "모아 설치";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Malgun Gothic", 9.5f);
        BackColor = Color.FromArgb(255, 253, 250);

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(20), Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 372));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var head = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 14) };
        var logo = new PictureBox { Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom, Image = Image.FromStream(new MemoryStream(MoaSetup.ReadResource("moa-logo.png"))) };
        var title = new Label { Text = "모아", AutoSize = true, Font = new Font("Malgun Gothic", 18f, FontStyle.Bold), Margin = new Padding(10, 11, 0, 0) };
        head.Controls.Add(logo);
        head.Controls.Add(title);
        layout.Controls.Add(head, 0, 0);
        layout.SetColumnSpan(head, 2);

        var folderLabel = new Label { Text = "설치 폴더", AutoSize = true, Margin = new Padding(0, 0, 0, 3) };
        layout.Controls.Add(folderLabel, 0, 1);
        layout.SetColumnSpan(folderLabel, 2);
        folder.Text = options.Dir ?? Options.DefaultDir;
        folder.Width = 364;
        folder.TextChanged += (s, e) => RefreshNote();
        layout.Controls.Add(folder, 0, 2);
        layout.Controls.Add(browse, 1, 2);
        desktop.Margin = new Padding(0, 10, 0, 6);
        layout.Controls.Add(desktop, 0, 3);
        layout.SetColumnSpan(desktop, 2);
        layout.Controls.Add(note, 0, 4);
        layout.SetColumnSpan(note, 2);
        status.Margin = new Padding(0, 8, 0, 8);
        layout.Controls.Add(status, 0, 5);
        layout.SetColumnSpan(status, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(action);
        layout.Controls.Add(buttons, 0, 6);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);

        browse.Click += (s, e) =>
        {
            using (var picker = new FolderBrowserDialog { Description = "모아를 설치할 폴더를 고르세요" })
                if (picker.ShowDialog(this) == DialogResult.OK) folder.Text = Path.Combine(picker.SelectedPath, "Moa");
        };
        cancel.Click += (s, e) => Close();
        action.Click += OnAction;
        AcceptButton = action;
        RefreshNote();
    }

    private void RefreshNote()
    {
        string dir = folder.Text.Trim(), oldData = null;
        try
        {
            if (Directory.Exists(Path.Combine(dir, "MoaData"))) { note.Text = "이 폴더에 있는 작업 데이터를 그대로 사용합니다."; return; }
            oldData = MoaSetup.FindOldData(options, dir);
        }
        catch (Exception) { }
        note.Text = oldData != null ? "기존 작업을 복사해 옵니다. 원본은 지우지 않습니다.\n" + oldData : "관리자 권한 없이 설치됩니다.";
    }

    private async void OnAction(object sender, EventArgs args)
    {
        string dir = folder.Text.Trim();
        if (installed)
        {
            action.Enabled = false;
            status.Text = "모아를 여는 중";
            try
            {
                using (Process app = Process.Start(new ProcessStartInfo(Path.Combine(dir, "Moa.exe")) { UseShellExecute = true, WorkingDirectory = dir }))
                {
                    await Task.Delay(4000);
                    if (app != null && !app.HasExited) { Close(); return; }
                }
                status.Text = "모아가 바로 종료되었습니다. 보안 프로그램이 막았을 수 있으니 다시 눌러 보세요. 계속되면 백신의 검사 예외에 설치 폴더를 추가해 주세요.";
            }
            catch (Exception error)
            {
                status.Text = "모아를 열지 못했습니다. 보안 프로그램이 막았을 수 있습니다. (" + error.Message + ")";
            }
            action.Enabled = true;
            return;
        }
        if (dir.Length == 0 || !Path.IsPathRooted(dir)) { status.Text = "설치할 폴더를 전체 경로로 입력해 주세요."; return; }
        foreach (Control control in new Control[] { action, cancel, browse, folder, desktop }) control.Enabled = false;
        bool makeDesktop = desktop.Checked;
        try
        {
            Action<string> report = text => BeginInvoke((Action)(() => status.Text = text));
            await Task.Run(() => MoaSetup.Install(options, dir, makeDesktop, report));
            installed = true;
            status.Text = "설치했습니다.";
            action.Text = "모아 실행";
            cancel.Text = "닫기";
            action.Enabled = cancel.Enabled = true;
        }
        catch (Exception error)
        {
            status.Text = error is UnauthorizedAccessException ? "이 폴더에는 설치할 수 없습니다. 다른 폴더를 골라 주세요." : error.Message;
            foreach (Control control in new Control[] { action, cancel, browse, folder, desktop }) control.Enabled = true;
        }
    }
}
#endif
