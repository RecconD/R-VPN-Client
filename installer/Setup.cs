using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using Forms = System.Windows.Forms;
using RVpn;

[assembly: AssemblyTitle("R VPN Windows Setup")]

internal static class Setup
{
    internal const string AppFile = "R VPN.exe";
    internal const string UninstallFile = "R VPN Uninstall.exe";
    private const string Marker = ".rvpn-install";
    private const string MarkerValue = "R VPN installed application v1";
    private static readonly string[] PackageFiles = {
        AppFile, AppFile + ".config", "README.md", "RVPN.Settings.json",
        "core/RVPN.EngineHost.exe", "core/sing-box.exe", "core/sing-box-LICENSE.txt",
        "core/wintun.dll", "core/wintun-LICENSE.txt", "fonts/Comfortaa-Regular.ttf",
        "fonts/Comfortaa-SemiBold.ttf", "fonts/Comfortaa-Bold.ttf", "fonts/Comfortaa-OFL.txt",
        "fonts/BebasNeue-Regular.ttf", "fonts/BebasNeue-OFL.txt"
    };
    private static readonly string[] AllFiles = PackageFiles.Concat(new[] { UninstallFile, Marker }).ToArray();
    private static readonly string ProfileDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R VPN");
    internal static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RVPN");

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2)
        {
            try
            {
#if UNINSTALL
                if (args[0] == "--remove-files") { RemoveFiles(args[1]); return 0; }
#else
                if (args[0] == "--install-files") { InstallFiles(args[1], new Progress<int>()); return 0; }
#endif
            }
            catch (Exception error)
            {
                BrandDialog.Notify(null, "R VPN", error.Message);
                return 1;
            }
        }
        int processId;
        if (args.Length == 4 && args[0] == "--remove" && int.TryParse(args[2], out processId))
            return RemoveAndNotify(args[1], processId, args[3] == "1");
#if UNINSTALL
        var directory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!IsInstalled(directory))
        {
            BrandDialog.Notify(null, "R VPN", "Запустите удаление из папки R VPN.");
            return 1;
        }
        var window = new SetupWindow(true, directory);
#else
        var window = new SetupWindow(false, DefaultDirectory);
#endif
        return new Application().Run(window);
    }

    internal static Geometry ReadLogo()
    {
        using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.svg"))
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.Load(source);
            var path = xml.DocumentElement.SelectSingleNode("*[local-name()='path']");
            return Geometry.Parse(path.Attributes["d"].Value);
        }
    }

    private static bool IsInstalled(string path)
    {
        var marker = Path.Combine(path, Marker);
        return File.Exists(marker) && new FileInfo(marker).Length < 100 && File.ReadAllText(marker) == MarkerValue;
    }

    internal static void StartApp(string folder)
    {
        Process.Start(new ProcessStartInfo(Path.Combine(folder, AppFile)) { WorkingDirectory = folder });
    }

    internal static void Install(string path, bool desktop, bool startMenu, IProgress<int> progress)
    {
        path = Path.GetFullPath(path);
        if (NeedsElevation(path))
        {
            progress.Report(15);
            RunElevated("--install-files", path);
        }
        else InstallFiles(path, progress);
        progress.Report(90);
        if (desktop) WriteShortcut(Environment.SpecialFolder.DesktopDirectory, path);
        if (startMenu) WriteShortcut(Environment.SpecialFolder.Programs, path);
        progress.Report(100);
    }

    private static bool NeedsElevation(string path)
    {
        using (var identity = WindowsIdentity.GetCurrent())
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return false;
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath).Where(root => !String.IsNullOrEmpty(root))
            .Any(root => full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void RunElevated(string action, string path)
    {
        try
        {
            using (var helper = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                action + " \"" + path.TrimEnd(Path.DirectorySeparatorChar) + "\"")
                { UseShellExecute = true, Verb = "runas" }))
            {
                if (helper == null) throw new IOException("Не удалось запустить операцию от имени администратора.");
                helper.WaitForExit();
                if (helper.ExitCode != 0) throw new IOException("Не удалось изменить файлы R VPN.");
            }
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            if (error.NativeErrorCode == 1223) throw new IOException("Запрос прав администратора отменён.");
            throw;
        }
    }

    private static void InstallFiles(string path, IProgress<int> progress)
    {
        path = Path.GetFullPath(path);
        if (Path.GetPathRoot(path) == path || IsLinked(path))
            throw new IOException("Выберите обычную папку для R VPN.");
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any() && !IsInstalled(path))
            throw new IOException("Выберите пустую папку или существующую папку установки R VPN.");
        if (Process.GetProcessesByName("R VPN").Any())
            throw new IOException("Закройте R VPN перед установкой.");

        var stage = Path.Combine(Path.GetTempPath(), "rvpn-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.zip"))
            using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
            {
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/", StringComparison.Ordinal)) continue;
                    if (!name.StartsWith("R VPN/", StringComparison.Ordinal)) throw new InvalidDataException();
                    var relative = name.Substring(6);
                    if (!PackageFiles.Contains(relative) || !found.Add(relative)) throw new InvalidDataException();
                    var destination = Path.Combine(stage, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination);
                    progress.Report(found.Count * 70 / PackageFiles.Length);
                }
                if (found.Count != PackageFiles.Length) throw new InvalidDataException();
            }
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("uninstall.exe"))
            using (var file = File.Create(Path.Combine(stage, UninstallFile))) source.CopyTo(file);
            File.WriteAllText(Path.Combine(stage, Marker), MarkerValue);
            Commit(stage, path);
            progress.Report(90);
        }
        finally
        {
            RemoveKnownFiles(stage, AllFiles);
        }
    }

    private static void Commit(string stage, string path)
    {
        Directory.CreateDirectory(path);
        var updated = new List<string>();
        try
        {
            foreach (var relative in AllFiles)
            {
                var destination = Path.Combine(path, relative);
                if (relative == "RVPN.Settings.json" && File.Exists(destination)) continue;
                if (IsLinked(destination)) throw new IOException("Папка установки содержит ссылку.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                var next = destination + ".rvpn-new";
                var backup = destination + ".rvpn-backup";
                if (File.Exists(next) || File.Exists(backup)) throw new IOException("Завершите предыдущую установку.");
                File.Copy(Path.Combine(stage, relative), next);
                try
                {
                    if (File.Exists(destination)) File.Replace(next, destination, backup, true);
                    else File.Move(next, destination);
                }
                finally { if (File.Exists(next)) File.Delete(next); }
                updated.Add(relative);
            }
        }
        catch
        {
            foreach (var relative in Enumerable.Reverse(updated))
            {
                var destination = Path.Combine(path, relative);
                var backup = destination + ".rvpn-backup";
                if (File.Exists(backup)) File.Replace(backup, destination, null, true);
                else File.Delete(destination);
            }
            throw;
        }
        foreach (var relative in updated)
        {
            var backup = Path.Combine(path, relative) + ".rvpn-backup";
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    private static bool IsLinked(string path)
    {
        while (!String.IsNullOrEmpty(path))
        {
            if ((Directory.Exists(path) || File.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return true;
            path = Path.GetDirectoryName(path);
        }
        return false;
    }

    private static void WriteShortcut(Environment.SpecialFolder folder, string directory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType);
        var link = Path.Combine(Environment.GetFolderPath(folder), "R VPN.lnk");
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = Path.Combine(directory, AppFile);
        shortcut.IconLocation = Path.Combine(directory, AppFile) + ",0";
        shortcut.WorkingDirectory = directory;
        shortcut.Save();
        WindowsShell.UpdateShortcutIdentity(link);
    }

    private static void RemoveShortcut(Environment.SpecialFolder folder, string directory)
    {
        var link = Path.Combine(Environment.GetFolderPath(folder), "R VPN.lnk");
        if (!File.Exists(link)) return;
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(link);
        if (String.Equals((string)shortcut.TargetPath, Path.Combine(directory, AppFile), StringComparison.OrdinalIgnoreCase))
            File.Delete(link);
    }

    internal static void Uninstall(string path, bool removeProfile)
    {
        if (Process.GetProcessesByName("R VPN").Any())
            throw new IOException("Закройте R VPN перед удалением.");
        var helper = Path.Combine(Path.GetTempPath(), "rvpn-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, helper);
        Process.Start(new ProcessStartInfo(helper,
            "--remove \"" + path.TrimEnd(Path.DirectorySeparatorChar) + "\" " + Process.GetCurrentProcess().Id + " " + (removeProfile ? "1" : "0"))
        { UseShellExecute = false, WorkingDirectory = Path.GetTempPath(), WindowStyle = ProcessWindowStyle.Hidden });
    }

    private static int RemoveAndNotify(string path, int processId, bool removeProfile)
    {
        try
        {
            try { using (var owner = Process.GetProcessById(processId)) if (!owner.WaitForExit(10000)) return 1; }
            catch (ArgumentException) { }
            if (NeedsElevation(path)) RunElevated("--remove-files", path);
            else RemoveFiles(path);
            RemoveShortcut(Environment.SpecialFolder.DesktopDirectory, path);
            RemoveShortcut(Environment.SpecialFolder.Programs, path);
            if (removeProfile) RemoveKnownFiles(ProfileDirectory, new[] { "Client/state.dat", "Preview/state.dat" });
            BrandDialog.Notify(null, "R VPN", "R VPN удалён.");
            return 0;
        }
        catch (Exception)
        {
            BrandDialog.Notify(null, "R VPN", "Не удалось завершить удаление. Закройте R VPN и повторите.");
            return 1;
        }
    }

    private static void RemoveFiles(string path)
    {
        path = Path.GetFullPath(path);
        if (IsLinked(path) || !IsInstalled(path))
            throw new IOException("Выберите действительную папку установки R VPN.");
        if (Process.GetProcessesByName("R VPN").Any())
            throw new IOException("Закройте R VPN перед удалением.");
        RemoveKnownFiles(path, AllFiles);
    }

    private static void RemoveKnownFiles(string path, IEnumerable<string> names)
    {
        if (IsLinked(path)) return;
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var file = Path.Combine(path, name);
            if (IsLinked(file)) continue;
            if (File.Exists(file)) File.Delete(file);
            directories.Add(Path.GetDirectoryName(file));
        }
        foreach (var directory in directories.OrderByDescending(item => item.Length))
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }
}

internal sealed class SetupWindow : Window
{
    private static readonly Brush Gold = Paint("#FED215"), Muted = Paint("#AAA9A1"), Ink = Paint("#F2F1EA"), Line = Paint("#41403B");
    private readonly StackPanel panel = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
    private readonly StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly bool removing;
    private string folder;
    private int stage;
    private bool desktop = true, startMenu = true;
    private bool automaticUpdates, connectionDiagnostics;
    private bool busy;

    internal SetupWindow(bool removing, string folder)
    {
        this.removing = removing;
        this.folder = folder;
        Title = removing ? "R VPN · Удаление" : "R VPN · Установка";
        Width = 530;
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        Foreground = Ink;
        FontFamily = new FontFamily("Segoe UI");
        Closing += (sender, args) => args.Cancel = busy;

        var content = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(16, 8, 12, 8), Background = Brushes.Transparent };
        var close = Button("×", false);
        close.MinWidth = 30;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.ToolTip = "Закрыть";
        close.Click += (sender, args) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var system = new TextBlock { Text = "Windows", Foreground = Muted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        DockPanel.SetDock(system, Dock.Right);
        header.Children.Add(system);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = Setup.ReadLogo();
        Icon = new DrawingImage(new GeometryDrawing(Gold, null, logo));
        brand.Children.Add(new System.Windows.Shapes.Path { Data = logo, Fill = Gold, Width = 21, Height = 21,
            Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) });
        brand.Children.Add(new TextBlock { Text = "R VPN", FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(brand);
        header.MouseLeftButtonDown += (sender, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        var headerFrame = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
        DockPanel.SetDock(headerFrame, Dock.Top);
        content.Children.Add(headerFrame);

        var footer = new DockPanel { Margin = new Thickness(24, 12, 24, 12) };
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        footer.Children.Add(new TextBlock { Text = "R VPN для Windows", Foreground = Muted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center });
        var footerFrame = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), MinHeight = 69, Child = footer };
        DockPanel.SetDock(footerFrame, Dock.Bottom);
        content.Children.Add(footerFrame);
        content.Children.Add(panel);
        Content = new Border
        {
            CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), BorderBrush = Line,
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#323232"),
                (Color)ColorConverter.ConvertFromString("#1A1A19"), new Point(0, 0), new Point(1, 1)),
            Child = content
        };
        Render();
    }

    private static Brush Paint(string color)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void Head(string kicker, string title, string note)
    {
        panel.Children.Clear();
        actions.Children.Clear();
        panel.Children.Add(new TextBlock { Text = kicker, Foreground = Gold, FontSize = 11,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 5) });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 29, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = note, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 12) });
    }

    private static CheckBox Option(string text, bool checkedValue)
    {
        return new CheckBox { Content = text, IsChecked = checkedValue, Foreground = Ink, Margin = new Thickness(0, 5, 0, 0) };
    }

    private static Button Button(string label, bool primary)
    {
        var button = new Button { Content = label, Height = 37, MinWidth = 76, Cursor = Cursors.Hand,
            Padding = new Thickness(13, 4, 13, 4), FontWeight = FontWeights.SemiBold,
            Foreground = primary ? Paint("#221B09") : Ink, Background = primary ? new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FED215"), (Color)ColorConverter.ConvertFromString("#EAC102"), 0) : Paint("#242423"),
            BorderBrush = primary ? Paint("#FCE581") : Paint("#464642"), BorderThickness = new Thickness(1),
            Margin = new Thickness(7, 0, 0, 0) };
        var shape = new FrameworkElementFactory(typeof(Border));
        shape.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        shape.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource =
            new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        shape.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource =
            new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        shape.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        shape.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource =
            new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var labelView = new FrameworkElementFactory(typeof(ContentPresenter));
        labelView.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        labelView.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        shape.AppendChild(labelView);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = shape };
        return button;
    }

    private void AddAction(string label, bool primary, Action handler)
    {
        var button = Button(label, primary);
        button.Click += (sender, args) => handler();
        actions.Children.Add(button);
    }

    private void Render()
    {
        if (removing)
        {
            Head("УПРАВЛЕНИЕ ПРИЛОЖЕНИЕМ", "Удаление R VPN", "Установленные файлы и ярлыки будут удалены.");
            var profile = Option("Удалить активацию и настройки на этом ПК", true);
            panel.Children.Add(profile);
            AddAction("Отмена", false, Close);
            AddAction("Удалить", true, () =>
            {
                try { Setup.Uninstall(folder, profile.IsChecked == true); Close(); }
                catch (Exception error) { BrandDialog.Notify(this, "R VPN", error.Message); }
            });
            return;
        }
        if (stage == 0)
        {
            Head("ШАГ 1 ИЗ 4", "Установка R VPN", "Установим приложение для Windows 10 и 11.");
            panel.Children.Add(new TextBlock { Text = "При подключении создаётся VPN-адаптер, меняются маршруты и DNS. Для этого Windows запросит права администратора.",
                TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0,0,0,9) });
            string policy;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PRIVACY.md"))
            using (var reader = new StreamReader(stream)) policy = reader.ReadToEnd();
            panel.Children.Add(new ScrollViewer { Height = 175, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock { Text = policy, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Ink } });
            var updates = Option("Проверять обновления автоматически", automaticUpdates);
            var diagnostics = Option("Отправлять результат подключения (порт и доступность)", connectionDiagnostics);
            panel.Children.Add(updates);
            panel.Children.Add(diagnostics);
            AddAction("Отмена", false, Close);
            AddAction("Далее", true, () => { automaticUpdates = updates.IsChecked == true; connectionDiagnostics = diagnostics.IsChecked == true; stage = 1; Render(); });
        }
        else if (stage == 1)
        {
            Head("ШАГ 2 ИЗ 4", "Папка и ярлыки", "Куда установить R VPN?");
            var row = new DockPanel();
            var browse = Button("Обзор…", false);
            var path = new TextBox { Text = folder, Padding = new Thickness(9, 5, 9, 5), Height = 35,
                Background = Paint("#191918"), Foreground = Ink, BorderBrush = Paint("#464642") };
            browse.Click += (sender, args) =>
            {
                using (var picker = new Forms.FolderBrowserDialog { Description = "Папка установки R VPN",
                    ShowNewFolderButton = true, SelectedPath = Directory.Exists(path.Text) ? path.Text : "" })
                    if (picker.ShowDialog() == Forms.DialogResult.OK)
                    {
                        path.Text = picker.SelectedPath;
                        if (Directory.EnumerateFileSystemEntries(path.Text).Any() &&
                            !File.Exists(Path.Combine(path.Text, ".rvpn-install")))
                            path.Text = Path.Combine(path.Text, "R VPN");
                    }
            };
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse);
            row.Children.Add(path);
            panel.Children.Add(row);
            var desktopBox = Option("Ярлык на рабочем столе", desktop);
            var startBox = Option("Ярлык в меню «Пуск»", startMenu);
            panel.Children.Add(desktopBox);
            panel.Children.Add(startBox);
            AddAction("Назад", false, () => { stage = 0; Render(); });
            AddAction("Установить", true, async () =>
            {
                folder = path.Text.Trim();
                desktop = desktopBox.IsChecked == true;
                startMenu = startBox.IsChecked == true;
                await Install();
            });
        }
        else
        {
            Head("ШАГ 4 ИЗ 4", "R VPN установлен", "Можно запустить приложение и ввести ключ активации.");
            var launch = Option("Запустить R VPN", true);
            panel.Children.Add(launch);
            AddAction("Готово", true, () =>
            {
                if (launch.IsChecked == true) Setup.StartApp(folder);
                Close();
            });
        }
    }

    private async Task Install()
    {
        busy = true;
        Head("ШАГ 3 ИЗ 4", "Устанавливаем R VPN", "Копируем файлы приложения.");
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Foreground = Gold,
            Background = Paint("#141414"), Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(progress);
        var detail = new TextBlock { Text = "0% · Подготавливаем файлы", Foreground = Muted, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(detail);
        try
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new IOException("Укажите папку установки.");
            var indicator = new Progress<int>(value =>
            {
                progress.Value = value;
                detail.Text = value + "% · Подготавливаем файлы";
            });
            await Task.Run(() => Setup.Install(folder, desktop, startMenu, indicator));
            var privacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R VPN");
            Directory.CreateDirectory(privacy);
            File.WriteAllText(Path.Combine(privacy, "privacy.json"), "{\"automaticUpdateChecks\":" + (automaticUpdates ? "true" : "false") +
                ",\"connectionDiagnostics\":" + (connectionDiagnostics ? "true" : "false") + "}");
            stage = 3;
        }
        catch (Exception error)
        {
            BrandDialog.Notify(this, "R VPN", error.Message);
            stage = 1;
        }
        finally { busy = false; }
        Render();
    }
}
