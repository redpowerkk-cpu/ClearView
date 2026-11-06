using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
    static readonly string InstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClearView");
    static readonly string MenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ClearView");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClearView";
    [STAThread] static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
#if UNINSTALL
            if (args.Length == 1 && args[0] == "--remove") { Remove(); return 0; }
            if (MessageBox.Show("ClearView를 제거할까요?\n사용자 사진 파일은 변경하지 않습니다.", "ClearView 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            EnsureClosed();
            string temporary = Path.Combine(Path.GetTempPath(), "ClearView-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, temporary);
            Process.Start(new ProcessStartInfo(temporary, "--remove") { UseShellExecute = false }); return 0;
#else
            if (args.Length == 2 && args[0] == "--extract") { Extract(Path.GetFullPath(args[1])); return 0; }
            Application.Run(new InstallForm()); return 0;
#endif
        }
        catch (Exception e) { if (args.Length > 0 && args[0] == "--extract") return 1; MessageBox.Show(e.Message, "ClearView", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
    static void EnsureClosed()
    {
        foreach (Process p in Process.GetProcessesByName("ClearView"))
        {
            try { if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), InstallPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("ClearView를 종료한 후 다시 시도해 주세요."); }
            catch (System.ComponentModel.Win32Exception) { throw new IOException("실행 중인 ClearView를 종료한 후 다시 시도해 주세요."); }
            finally { p.Dispose(); }
        }
    }
    static void Extract(string target)
    {
        Directory.CreateDirectory(target);
        string prefix = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                string output = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("설치 파일 경로가 올바르지 않습니다.");
                if (entry.Name.Length == 0) { Directory.CreateDirectory(output); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var input = entry.Open()) using (var file = File.Create(output)) input.CopyTo(file);
            }
        }
    }
    static void Shortcut(string destination, string executable)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(type);
        object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { destination });
        try
        {
            Type t = link.GetType(); t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { executable });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { InstallPath });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { executable + ",0" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    static void Install(bool desktop)
    {
        EnsureClosed();
        string staging = Path.Combine(Path.GetTempPath(), "ClearView-Install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Extract(staging); Directory.CreateDirectory(InstallPath);
            foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(InstallPath, file.Substring(staging.Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(file, dest, true);
            }
            Directory.CreateDirectory(MenuPath); Shortcut(Path.Combine(MenuPath, "ClearView.lnk"), Path.Combine(InstallPath, "ClearView.exe"));
            if (desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ClearView.lnk"), Path.Combine(InstallPath, "ClearView.exe"));
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "ClearView"); key.SetValue("DisplayVersion", "0.1.0"); key.SetValue("InstallLocation", InstallPath);
                key.SetValue("DisplayIcon", Path.Combine(InstallPath, "ClearView.exe")); key.SetValue("UninstallString", "\"" + Path.Combine(InstallPath, "Uninstall.exe") + "\"");
                key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                long bytes = 0; foreach (string f in Directory.GetFiles(InstallPath, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
                key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    static void Remove()
    {
        System.Threading.Thread.Sleep(600); EnsureClosed();
        string manifest = Path.Combine(InstallPath, "installed-files.txt");
        if (!File.Exists(manifest)) throw new IOException("설치 파일 목록을 찾을 수 없습니다. 같은 버전을 다시 설치한 후 제거해 주세요.");
        string prefix = Path.GetFullPath(InstallPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string relative in File.ReadAllLines(manifest))
        {
            string file = Path.GetFullPath(Path.Combine(InstallPath, relative));
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("설치 파일 목록이 올바르지 않습니다.");
            if (File.Exists(file)) File.Delete(file);
        }
        if (File.Exists(manifest)) File.Delete(manifest);
        foreach (string directory in Directory.GetDirectories(InstallPath, "*", SearchOption.AllDirectories)) { if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory); }
        if (Directory.GetFileSystemEntries(InstallPath).Length == 0) Directory.Delete(InstallPath);
        string start = Path.Combine(MenuPath, "ClearView.lnk"), desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ClearView.lnk");
        if (File.Exists(start)) File.Delete(start); if (File.Exists(desk)) File.Delete(desk);
        if (Directory.Exists(MenuPath) && Directory.GetFileSystemEntries(MenuPath).Length == 0) Directory.Delete(MenuPath);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        MessageBox.Show("ClearView를 제거했습니다.", "ClearView", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    sealed class InstallForm : Form
    {
        readonly Button action = new Button(); readonly Label status = new Label(); readonly CheckBox desktop = new CheckBox(); bool done, installing;
        public InstallForm()
        {
            Text = "ClearView 설치"; ClientSize = new Size(540, 350); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Font = new Font("Malgun Gothic", 9); BackColor = Color.FromArgb(25, 29, 32); ForeColor = Color.FromArgb(237, 242, 240);
            var title = new Label { Text = "ClearView", Font = new Font("Segoe UI", 27), Location = new Point(32, 24), Size = new Size(470, 55) };
            var description = new Label { Text = "큰 사진도 빠르게 보는 가벼운 윈도우 사진 뷰어\n사진을 열고, 확대하고, 전체화면으로 감상하세요.", Location = new Point(34, 94), Size = new Size(470, 55) };
            var path = new Label { Text = "설치 위치\n" + InstallPath, Location = new Point(34, 161), Size = new Size(470, 48), AutoEllipsis = true, ForeColor = Color.FromArgb(166, 180, 177) };
            desktop.Text = "바탕 화면에 바로가기 만들기"; desktop.Checked = true; desktop.SetBounds(34, 215, 400, 28);
            status.Text = "현재 사용자 계정에 설치합니다.  ·  버전 0.1.0"; status.SetBounds(34, 260, 470, 25);
            action.Text = "설치"; action.SetBounds(376, 298, 128, 36); action.FlatStyle = FlatStyle.Flat; action.BackColor = Color.FromArgb(139, 226, 194); action.ForeColor = BackColor; action.FlatAppearance.BorderSize = 0;
            action.Click += async delegate
            {
                if (done) { Process.Start(Path.Combine(InstallPath, "ClearView.exe")); Close(); return; }
                installing = true; action.Enabled = false; desktop.Enabled = false; status.Text = "설치 중… 잠시만 기다려 주세요.";
                bool makeDesktop = desktop.Checked;
                try { await Task.Run(delegate { Install(makeDesktop); }); done = true; action.Text = "사진 뷰어 열기"; status.Text = "설치가 완료되었습니다."; }
                catch (Exception e) { status.Text = "설치하지 못했습니다. 다시 시도해 주세요."; MessageBox.Show(this, e.Message, "ClearView 설치", MessageBoxButtons.OK, MessageBoxIcon.Error); desktop.Enabled = true; }
                finally { installing = false; action.Enabled = true; }
            };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (installing) e.Cancel = true; };
            Controls.AddRange(new Control[] { title, description, path, desktop, status, action }); AcceptButton = action;
        }
    }
}
