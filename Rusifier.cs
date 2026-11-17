using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

static class Installer
{
    const string AppId = "1360980";

    public static bool IsGame(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        return File.Exists(Path.Combine(dir, "game", "scripts.rpa"))
            && File.Exists(Path.Combine(dir, "FetishLocator.exe"));
    }

    static string Target(string dir)
    {
        return Path.Combine(dir, "game", "tl", "russian");
    }

    public static bool IsInstalled(string dir)
    {
        return Directory.Exists(Target(dir));
    }

    public static bool GameRunning()
    {
        return Process.GetProcessesByName("FetishLocator").Length > 0;
    }

    public static string FindGame()
    {
        var libs = new List<string>();
        foreach (var steam in SteamRoots())
        {
            if (!libs.Contains(steam)) libs.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace("\\\\", "\\");
                if (!libs.Contains(p)) libs.Add(p);
            }
        }

        foreach (var lib in libs)
        {
            var manifest = Path.Combine(lib, "steamapps", "appmanifest_" + AppId + ".acf");
            if (!File.Exists(manifest)) continue;
            var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!m.Success) continue;
            var dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
            if (IsGame(dir)) return dir;
        }
        return "";
    }

    static IEnumerable<string> SteamRoots()
    {
        var found = new List<string>();
        string[][] keys =
        {
            new[] { "HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath" },
            new[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam", "InstallPath" },
            new[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam", "InstallPath" },
        };
        foreach (var k in keys)
        {
            try
            {
                var v = Registry.GetValue(k[0], k[1], null) as string;
                if (string.IsNullOrEmpty(v)) continue;
                v = Path.GetFullPath(v.Replace('/', '\\'));
                if (Directory.Exists(v) && !found.Contains(v)) found.Add(v);
            }
            catch (Exception) { }
        }
        return found;
    }

    public static int Install(string dir)
    {
        if (!IsGame(dir)) throw new InvalidOperationException("В этой папке нет Fetish Locator Week One.");

        var target = Path.GetFullPath(Target(dir));
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(target);

        int count = 0;
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('/', '\\');
                if (name.EndsWith("\\") || name.Length == 0) continue;

                var dest = Path.GetFullPath(Path.Combine(target, name));
                if (!dest.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (var src = entry.Open())
                using (var dst = File.Create(dest))
                {
                    src.CopyTo(dst);
                }
                count++;
            }
        }
        return count;
    }

    public static bool Remove(string dir)
    {
        if (!IsGame(dir)) throw new InvalidOperationException("В этой папке нет Fetish Locator Week One.");
        var target = Target(dir);
        if (!Directory.Exists(target)) return false;
        Directory.Delete(target, true);
        return true;
    }
}

class MainForm : Form
{
    readonly TextBox path = new TextBox();
    readonly Label status = new Label();
    readonly Button install = new Button();
    readonly Button remove = new Button();

    public MainForm()
    {
        Text = "Русификатор Fetish Locator Week One";
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(560, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var intro = new Label
        {
            Text = "Возвращает русский язык в актуальную Steam-версию игры.\n"
                 + "Файлы игры не заменяются: перевод кладётся в папку game\\tl\\russian.",
            Location = new Point(16, 14),
            Size = new Size(528, 40),
        };

        var pathLabel = new Label { Text = "Папка с игрой:", Location = new Point(16, 66), AutoSize = true };
        path.Location = new Point(16, 88);
        path.Size = new Size(430, 25);
        path.TextChanged += delegate { Refresh2(); };

        var browse = new Button { Text = "Обзор...", Location = new Point(454, 86), Size = new Size(90, 28) };
        browse.Click += delegate
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Укажите папку, в которой лежит FetishLocator.exe";
                dlg.SelectedPath = path.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) path.Text = dlg.SelectedPath;
            }
        };

        status.Location = new Point(16, 124);
        status.Size = new Size(528, 44);

        install.Location = new Point(16, 176);
        install.Size = new Size(260, 36);
        install.Click += delegate { Run(true); };

        remove.Text = "Удалить русификатор";
        remove.Location = new Point(284, 176);
        remove.Size = new Size(260, 36);
        remove.Click += delegate { Run(false); };

        var credit = new Label
        {
            Text = "Перевод: Simfer (версия 2021 года) + дополнения под текущую версию.",
            Location = new Point(16, 222),
            Size = new Size(528, 20),
            ForeColor = SystemColors.GrayText,
        };

        Controls.AddRange(new Control[] { intro, pathLabel, path, browse, status, install, remove, credit });
        path.Text = Installer.FindGame();
        Refresh2();
    }

    void Refresh2()
    {
        var dir = path.Text.Trim();
        var ok = Installer.IsGame(dir);
        var has = ok && Installer.IsInstalled(dir);

        install.Enabled = ok;
        remove.Enabled = has;
        install.Text = has ? "Переустановить русификатор" : "Установить русификатор";

        if (dir.Length == 0)
            status.Text = "Игра не найдена автоматически. Укажите папку с FetishLocator.exe.";
        else if (!ok)
            status.Text = "В этой папке нет игры: нужны FetishLocator.exe и game\\scripts.rpa.";
        else if (has)
            status.Text = "Игра найдена. Русификатор уже установлен.";
        else
            status.Text = "Игра найдена. Русификатор не установлен.";
    }

    void Run(bool doInstall)
    {
        var dir = path.Text.Trim();
        if (Installer.GameRunning())
        {
            MessageBox.Show(this, "Сначала закройте игру.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (doInstall)
            {
                int n = Installer.Install(dir);
                MessageBox.Show(this,
                    "Готово, скопировано файлов: " + n + ".\nРусский включится сам при следующем запуске игры.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Installer.Remove(dir);
                MessageBox.Show(this, "Русификатор удалён, игра снова на английском.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Нет прав на запись в папку игры. Запустите русификатор от имени администратора.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Не получилось: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Refresh2();
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Тихий режим: Rusifier.exe /install "папка игры"  или  /remove "папка игры"
        if (args.Length == 2 && (args[0] == "/install" || args[0] == "/remove"))
        {
            try
            {
                if (args[0] == "/install") Installer.Install(args[1]);
                else Installer.Remove(args[1]);
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
