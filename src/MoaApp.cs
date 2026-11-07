using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class MoaApp
{
    // 저장 데이터는 주소별로 보관되므로 이 주소를 바꾸면 기존 작업이 보이지 않는다.
    // 실제 서버나 포트는 없고, 이 주소로 가는 요청에 프로그램이 직접 응답한다.
    internal const string Origin = "http://127.0.0.1:49783";

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);

    // 마지막 실제 키보드·마우스 입력 후 지난 시간. 창이 왜 닫혔는지 기록에서 가려내는 데 쓴다.
    internal static long IdleMilliseconds()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
        return GetLastInputInfo(ref info) ? (long)unchecked((uint)Environment.TickCount - info.Time) : -1L;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += LoadEmbeddedAssembly;
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("처리되지 않은 오류: " + e.ExceptionObject);
        Application.ThreadException += (s, e) => { Log("창 오류: " + e.Exception); ShowError(e.Exception); };
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            return Run();
        }
        catch (Exception error)
        {
            if (args.Length != 0) return 1;
            ShowError(error);
            return 1;
        }
    }

    internal static void ShowError(Exception error)
    {
        Log("오류: " + error);
        string message = error is UnauthorizedAccessException
            ? "이 폴더에 저장할 수 없습니다. 문서 폴더처럼 쓰기 가능한 곳에서 실행해 주세요."
            : error.Message;
        MessageBox.Show(message, "모아", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string data = Path.Combine(root, "MoaData");
        bool first;
        using (var running = new Mutex(true, "Moa-" + Hash(data), out first))
        {
            string app = Path.Combine(data, "App");
            Directory.CreateDirectory(app);
            logPath = Path.Combine(app, "moa.log");
            Log("시작 first=" + first + " pid=" + Process.GetCurrentProcess().Id);
            if (!first)
            {
                if (ShowRunningWindow()) { Log("이미 열린 창을 앞으로 가져옴"); return 0; }
                // 창이 없으면 이전 실행이 닫히는 중(마지막 백업 중)이다. 끝나길 기다렸다 연다.
                Log("이전 실행 종료 대기");
                try { if (!running.WaitOne(25000)) { Log("이전 실행이 끝나지 않아 종료"); return 0; } }
                catch (AbandonedMutexException) { }
            }

            Directory.CreateDirectory(Path.Combine(data, "Originals"));
            Migrate(Path.Combine(data, "Browser"), Path.Combine(app, "EBWebView"));

            string loader = Path.Combine(app, "loader");
            Directory.CreateDirectory(loader);
            WriteLoader(Path.Combine(loader, "WebView2Loader.dll"));
            CoreWebView2Environment.SetLoaderDllFolderPath(loader);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(Path.Combine(data, "Backups"));
            Application.Run(new AppWindow(app, data, ReadResource("moa.html")));
            Log("창 루프 종료");
            GC.KeepAlive(running);
        }
        return 0;
    }

    private static bool ShowRunningWindow()
    {
        Process self = Process.GetCurrentProcess();
        foreach (Process other in Process.GetProcessesByName(self.ProcessName))
        {
            if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero) continue;
            if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9);
            SetForegroundWindow(other.MainWindowHandle);
            return true;
        }
        return false;
    }

    private static string logPath;

    internal static void Log(string message)
    {
        if (logPath == null) return;
        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 256 * 1024) File.Delete(logPath);
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + "\r\n");
        }
        catch (Exception) { }
    }

    // Edge 창 방식에서 쓰던 프로필을 복사만 한다. 원본은 지우거나 고치지 않는다.
    private static void Migrate(string oldProfile, string target)
    {
        string oldDefault = Path.Combine(oldProfile, "Default");
        if (Directory.Exists(target) || !Directory.Exists(oldDefault)) return;
        if (ProfileInUse(oldProfile))
            throw new InvalidOperationException("이전 모아 창이 아직 열려 있습니다. 그 창을 닫은 뒤 다시 실행해 주세요.");

        string temp = target + ".tmp";
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        CopyFolder(oldDefault, Path.Combine(temp, "Default"));
        string state = Path.Combine(oldProfile, "Local State");
        if (File.Exists(state)) File.Copy(state, Path.Combine(temp, "Local State"));
        Directory.Move(temp, target);
    }

    private static readonly string[] Caches = {
        "Cache", "Code Cache", "GPUCache", "DawnWebGPUCache", "DawnGraphiteCache", "DualEngine", "EdgeCoupons", "EntityExtraction"
    };

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (string folder in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(folder);
            if (!Caches.Contains(name, StringComparer.OrdinalIgnoreCase)) CopyFolder(folder, Path.Combine(to, name));
        }
    }

    private static bool ProfileInUse(string profile)
    {
        try
        {
            using (File.Open(Path.Combine(profile, "lockfile"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static void WriteLoader(string path)
    {
        byte[] bytes = ReadResource("WebView2Loader.dll");
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length) return;
        File.WriteAllBytes(path, bytes);
    }

    private static Assembly LoadEmbeddedAssembly(object sender, ResolveEventArgs request)
    {
        string name = new AssemblyName(request.Name).Name + ".dll";
        if (!name.StartsWith("Microsoft.Web.WebView2.", StringComparison.Ordinal)) return null;
        return Assembly.Load(ReadResource(name));
    }

    private static byte[] ReadResource(string name)
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var buffer = new MemoryStream())
        {
            if (source == null) throw new InvalidOperationException("모아 구성 파일을 불러오지 못했습니다: " + name);
            source.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private static string Hash(string text)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).Replace("-", "").Substring(0, 24);
    }

    private static int SelfTest()
    {
        string html = Encoding.UTF8.GetString(ReadResource("moa.html"));
        if (!html.Contains("모아 캔버스") || !html.Contains("free-editor")) return 2;
        foreach (string name in new[] { "WebView2Loader.dll", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll" })
            if (ReadResource(name).Length == 0) return 3;
        return 0;
    }
}

internal sealed class AppWindow : Form
{
    private const int BackupsToKeep = 30;
    private const long MaxRangeBytes = 8 * 1024 * 1024;
    private static readonly Regex OriginalName = new Regex(@"^moa-[a-z0-9.-]+\.(png|jpg|webp|gif|avif|mp4|webm|pdf|mp3|wav|ogg|m4a|weba)$", RegexOptions.IgnoreCase);
    private static readonly Regex BackupName = new Regex(@"^moa-backup-\d{8}-\d{6}\.json$");
    private readonly string userData;
    private readonly string originals;
    private readonly string backups;
    private readonly byte[] page;
    private readonly WebView2 web = new WebView2();
    private readonly TaskCompletionSource<bool> backupDone = new TaskCompletionSource<bool>();
    private bool closing;

    public AppWindow(string userData, string data, byte[] page)
    {
        this.userData = userData;
        originals = Path.Combine(data, "Originals");
        backups = Path.Combine(data, "Backups");
        this.page = page;
        FormClosing += OnFormClosing;
        Text = "모아";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        Rectangle screen = Screen.PrimaryScreen.WorkingArea;
        Size = new Size(Math.Min(1500, screen.Width), Math.Min(960, screen.Height));
        BackColor = Color.FromArgb(247, 243, 235);
        web.DefaultBackgroundColor = BackColor;
        web.Dock = DockStyle.Fill;
        Controls.Add(web);
        Shown += OnShown;
    }

    private async void OnShown(object sender, EventArgs args)
    {
        try
        {
            MoaApp.Log("WebView2 환경 생성");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await web.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = web.CoreWebView2;
            MoaApp.Log("WebView2 준비 완료");
            core.ProcessFailed += (s, e) => MoaApp.Log("WebView2 프로세스 실패: " + e.ProcessFailedKind + " " + e.Reason);
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.AddWebResourceRequestedFilter(MoaApp.Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += ServePage;
            core.NewWindowRequested += (s, e) => { e.Handled = true; OpenOutside(e.Uri); };
            core.NavigationStarting += (s, e) =>
            {
                if (e.Uri.StartsWith(MoaApp.Origin + "/", StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
                OpenOutside(e.Uri);
            };
            core.DocumentTitleChanged += (s, e) => { Text = core.DocumentTitle; };
            core.WebMessageReceived += OnPageMessage;
            core.Navigate(MoaApp.Origin + "/");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("모아를 열려면 Microsoft Edge WebView2 런타임이 필요합니다.", "모아", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception error)
        {
            MoaApp.ShowError(error);
            Close();
        }
    }

    // 창을 닫을 때 화면 쪽에 마지막 백업을 맡기고, 끝나거나 시간이 지나면 실제로 닫는다.
    private async void OnFormClosing(object sender, FormClosingEventArgs args)
    {
        MoaApp.Log("닫기 요청 " + args.CloseReason + " closing=" + closing + " ready=" + (web.CoreWebView2 != null) + " 입력후=" + MoaApp.IdleMilliseconds() + "ms 마우스(창기준)=" + PointToClient(MousePosition) + " 창폭=" + ClientSize.Width + " 키=" + ModifierKeys);
        if (closing || args.CloseReason == CloseReason.WindowsShutDown || web.CoreWebView2 == null) return;
        closing = true;
        args.Cancel = true;
        Hide();
        try
        {
            await web.CoreWebView2.ExecuteScriptAsync("window.moaHostBackup&&moaHostBackup(true)");
            await Task.WhenAny(backupDone.Task, Task.Delay(20000));
        }
        catch (Exception) { }
        Close();
    }

    private void OnPageMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.Source.StartsWith(MoaApp.Origin + "/", StringComparison.OrdinalIgnoreCase)) return;
        string message = args.TryGetWebMessageAsString();
        if (message == "backup-done") backupDone.TrySetResult(true);
        else if (message == "open-originals") OpenFolder(originals);
        else if (message == "open-backups") OpenFolder(backups);
    }

    private static void OpenFolder(string folder)
    {
        try { Process.Start("explorer.exe", "\"" + folder + "\""); }
        catch (Exception) { }
    }

    private CoreWebView2WebResourceResponse Reply(Stream body, int status, string reason, string type, string extra = "")
    {
        return web.CoreWebView2.Environment.CreateWebResourceResponse(body, status, reason,
            "Content-Type: " + type + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff" + extra);
    }

    // 영상이 필요한 부분만 요청할 수 있게 한다. 큰 파일도 전체를 읽지 않고 재생하고 건너뛸 수 있다.
    private CoreWebView2WebResourceResponse ReadOriginal(CoreWebView2WebResourceRequest request, string path, string name)
    {
        long length = new FileInfo(path).Length;
        string type = ContentType(name);
        string range = request.Headers.Contains("Range") ? request.Headers.GetHeader("Range") : null;
        Match match = range == null ? Match.Empty : Regex.Match(range, @"^bytes=(\d+)-(\d*)$");
        if (!match.Success)
            return Reply(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), 200, "OK", type, "\r\nAccept-Ranges: bytes\r\nContent-Length: " + length);

        long start = Int64.Parse(match.Groups[1].Value);
        if (start >= length)
            return Reply(new MemoryStream(new byte[0], false), 416, "Range Not Satisfiable", type, "\r\nContent-Range: bytes */" + length);
        long end = match.Groups[2].Length > 0 ? Math.Min(Int64.Parse(match.Groups[2].Value), length - 1) : length - 1;
        if (end < start)
            return Reply(new MemoryStream(new byte[0], false), 416, "Range Not Satisfiable", type, "\r\nContent-Range: bytes */" + length);
        // 한 번에 8MB까지만 보낸다. 나머지는 재생기가 이어서 요청한다.
        end = Math.Min(end, start + MaxRangeBytes - 1);
        var buffer = new byte[end - start + 1];
        int read = 0;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            file.Seek(start, SeekOrigin.Begin);
            while (read < buffer.Length)
            {
                int got = file.Read(buffer, read, buffer.Length - read);
                if (got == 0) break;
                read += got;
            }
        }
        return Reply(new MemoryStream(buffer, 0, read, false), 206, "Partial Content", type,
            "\r\nAccept-Ranges: bytes\r\nContent-Length: " + read + "\r\nContent-Range: bytes " + start + "-" + (start + read - 1) + "/" + length);
    }

    private CoreWebView2WebResourceResponse Status(int status, string reason)
    {
        return Reply(new MemoryStream(new byte[0], false), status, reason, "text/plain");
    }

    private static string ContentType(string name)
    {
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".png": return "image/png";
            case ".jpg": return "image/jpeg";
            case ".webp": return "image/webp";
            case ".gif": return "image/gif";
            case ".avif": return "image/avif";
            case ".mp4": return "video/mp4";
            case ".webm": return "video/webm";
            case ".pdf": return "application/pdf";
            case ".mp3": return "audio/mpeg";
            case ".wav": return "audio/wav";
            case ".ogg": return "audio/ogg";
            case ".m4a": return "audio/mp4";
            case ".weba": return "audio/webm";
            default: return "application/octet-stream";
        }
    }

    // /originals/<이름> 읽기·쓰기·지우기, /backups/<이름> 쓰기만 허용한다.
    private CoreWebView2WebResourceResponse ServeFile(CoreWebView2WebResourceRequest request, Uri uri, string folder, string name, bool isOriginal)
    {
        if (!(isOriginal ? OriginalName : BackupName).IsMatch(name)) return Status(400, "Bad Request");
        string path = Path.Combine(folder, name), part = path + ".part", method = request.Method.ToUpperInvariant();

        if (method == "GET" && isOriginal)
        {
            if (!File.Exists(path)) return Status(404, "Not Found");
            return ReadOriginal(request, path, name);
        }
        // 다른 출처의 페이지(영상 iframe 등)는 저장 폴더를 바꿀 수 없다.
        if (!request.Headers.Contains("Origin") || !String.Equals(request.Headers.GetHeader("Origin"), MoaApp.Origin, StringComparison.OrdinalIgnoreCase))
            return Status(403, "Forbidden");
        if (method == "DELETE" && isOriginal)
        {
            File.Delete(path);
            File.Delete(part);
            return Status(200, "OK");
        }
        if (method != "PUT") return Status(405, "Method Not Allowed");

        NameValue query = NameValue.Parse(uri.Query);
        if (query.Key == "offset")
        {
            long offset = Int64.Parse(query.Value);
            long current = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset != 0 && offset != current) return Status(409, "Conflict");
            using (var target = new FileStream(part, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write))
                if (request.Content != null) request.Content.CopyTo(target);
            return Status(200, "OK");
        }
        if (query.Key == "commit")
        {
            long size = Int64.Parse(query.Value);
            if (size == 0) File.WriteAllBytes(part, new byte[0]);
            if (!File.Exists(part) || new FileInfo(part).Length != size) return Status(409, "Conflict");
            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);
            if (!isOriginal) TrimBackups();
            return Status(200, "OK");
        }
        return Status(400, "Bad Request");
    }

    private void TrimBackups()
    {
        // 이름에 시각이 들어 있어 이름순이 곧 시간순이다.
        foreach (string old in Directory.GetFiles(backups, "moa-backup-*.json")
            .Where(file => BackupName.IsMatch(Path.GetFileName(file)))
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
            .Skip(BackupsToKeep))
            File.Delete(old);
    }

    private struct NameValue
    {
        public string Key;
        public string Value;
        public static NameValue Parse(string query)
        {
            string[] parts = query.TrimStart('?').Split('=');
            return new NameValue { Key = parts[0], Value = parts.Length == 2 ? parts[1] : "" };
        }
    }

    private void ServePage(object sender, CoreWebView2WebResourceRequestedEventArgs request)
    {
        Uri uri = new Uri(request.Request.Uri);
        string path = uri.AbsolutePath;
        foreach (var area in new[] { new { Prefix = "/originals/", Folder = originals, IsOriginal = true }, new { Prefix = "/backups/", Folder = backups, IsOriginal = false } })
        {
            if (!path.StartsWith(area.Prefix, StringComparison.Ordinal)) continue;
            try { request.Response = ServeFile(request.Request, uri, area.Folder, path.Substring(area.Prefix.Length), area.IsOriginal); }
            catch (Exception) { request.Response = Status(500, "Save Failed"); }
            return;
        }
        bool isPage = path == "/" || path == "/index.html";
        request.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream(isPage ? page : new byte[0], false),
            isPage ? 200 : 404,
            isPage ? "OK" : "Not Found",
            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
    }

    private static void OpenOutside(string address)
    {
        Uri uri;
        if (!Uri.TryCreate(address, UriKind.Absolute, out uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
