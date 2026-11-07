using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RVpn
{
    internal sealed class ReleaseManifest
    {
        public string version { get; set; }
        public string packageName { get; set; }
        public string sha256 { get; set; }
        public string signature { get; set; }

        internal Version Validate()
        {
            Version parsed;
            if (version == null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") ||
                !Version.TryParse(version + ".0", out parsed) ||
                packageName != "R-VPN-Windows-x64-" + version + ".zip" ||
                sha256 == null || !Regex.IsMatch(sha256, "^[a-f0-9]{64}$") ||
                signature == null || signature.Length > 600)
                throw new InvalidDataException("Некорректные данные обновления.");

            var signed = Encoding.UTF8.GetBytes(String.Join("\n", version, packageName, sha256));
            using (var key = new RSACryptoServiceProvider(new CspParameters(24)))
            {
                key.PersistKeyInCsp = false;
                key.FromXmlString("<RSAKeyValue><Modulus>0aKP4G6NtyAovvGEG0Mfid34uRfMRlRbRidFgWtJqeX45/slnqj+NSTR7/oWjyASTU5lpDNl831H0DDuXWtmwL/v9cfisfrQBUSKAEJTjVIPNaFJx2mnCw4StC6s4TF6WcTRwI8SGGCCiK0KHfBwfQzJXQPb7zRZwq4Q0jXsQtOn0o0QF/0EEHzYyYrxauKBZW049d5TBKXDmM1GMqfOr0invmQI98KdZwEapn6G7PpN6jh4vKdfqJ+SkqNecP76QhKXicizT/ptRI7qt0EwSiQae6CDZKFucYAovAHobZGeKbOlkZzSZIoLaGc9g+mPOFpBK7aejDhq/4MnE7VEP4s7H+hXWQfg6tXBA3Uis9fVgH4hX95DrEzILCdHanC7lDOsXT4M2n6w34fEDYE4ut/ZHTmGbyNW3HVPT9KEsokGlQfNAboQeehNOFIHQtsg4Huev0n4arcvfKp0zc19C6qlGCyXgxBP1iwW3hLjaZE8L+m1+VFLjzaH0ELR9/N7</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>");
                if (!key.VerifyData(signed, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(signature)))
                    throw new InvalidDataException("Подпись обновления не прошла проверку.");
            }
            return parsed;
        }

        internal void VerifyArchive(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!String.Equals(hash, sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Архив обновления не прошёл проверку.");
            }
        }
    }

    internal sealed class WindowsUpdate
    {
        private const string Executable = "R VPN.exe";
        private const string Manifest = "windows.json";
        private const string StagePrefix = ".rvpn-update-";
        private const long PackageLimit = 100 * 1024 * 1024;
        private static readonly string[] Files = {
            "R VPN.exe.config", "README.md", "core/RVPN.EngineHost.exe",
            "core/sing-box.exe", "core/sing-box-LICENSE.txt", "core/wintun.dll", "core/wintun-LICENSE.txt",
            "fonts/BebasNeue-OFL.txt", "fonts/BebasNeue-Regular.ttf", "fonts/Comfortaa-Bold.ttf",
            "fonts/Comfortaa-OFL.txt", "fonts/Comfortaa-Regular.ttf", "fonts/Comfortaa-SemiBold.ttf", Executable
        };
        private readonly Window owner;
        private readonly Uri service;
        private readonly string directory;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromHours(2) };
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Version offered;
        private Version approved;
        private bool busy;

        internal WindowsUpdate(Window owner, Uri service, string directory, Version approved = null, bool automatic = false)
        {
            this.owner = owner;
            this.service = service;
            this.directory = directory;
            this.approved = approved;
            timer.Tick += async (sender, args) => await Check();
            owner.Loaded += async (sender, args) =>
            {
                if (!automatic && this.approved == null) return;
                if (automatic) timer.Start();
                await Task.Delay(2400);
                if (!lifetime.IsCancellationRequested) await Check();
            };
            owner.Closed += (sender, args) =>
            {
                timer.Stop();
                lifetime.Cancel();
            };
        }

        internal async Task Check(Action<string> feedback = null)
        {
            if (lifetime.IsCancellationRequested) return;
            if (busy)
            {
                if (feedback != null) feedback("Проверка уже выполняется.");
                return;
            }
            busy = true;
            string stage = null;
            Window progress = null;
            if (feedback != null) feedback("Проверяем обновления…");
            try
            {
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }))
                {
                    client.Timeout = TimeSpan.FromMinutes(3);
                    client.MaxResponseContentBufferSize = 16384;
                    ReleaseManifest release;
                    using (var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        request.CancelAfter(TimeSpan.FromSeconds(15));
                        using (var response = await client.GetAsync(new Uri(service, "updates/" + Manifest), request.Token))
                        {
                            response.EnsureSuccessStatusCode();
                            release = DataJson.Read<ReleaseManifest>(await response.Content.ReadAsStringAsync());
                        }
                    }
                    {
                        if (release == null) throw new InvalidDataException("Пустой манифест обновления.");
                        var version = release.Validate();
                        if (version <= Assembly.GetExecutingAssembly().GetName().Version)
                        {
                            if (feedback != null) feedback("Установлена актуальная версия.");
                            return;
                        }
                        if (feedback == null && version == offered) return;
                        if (feedback != null) feedback("Доступна версия " + release.version + ".");
                        offered = version;
                        var alreadyApproved = version == approved;
                        approved = null;
                        if (!alreadyApproved && !Confirm(release.version)) return;
                        stage = Path.Combine(directory, StagePrefix + Guid.NewGuid().ToString("N"));
                        try { Directory.CreateDirectory(stage); }
                        catch (UnauthorizedAccessException)
                        {
                            if (Desktop.IsAdministrator()) throw;
                            stage = null;
                            using (var elevated = Process.Start(new ProcessStartInfo(Path.Combine(directory, Executable),
                                "--approved-update " + version) { WorkingDirectory = directory, UseShellExecute = true, Verb = "runas" }))
                            {
                                if (elevated == null) throw new IOException("Не удалось запустить обновление.");
                            }
                            owner.Close();
                            return;
                        }
                        progress = ProgressWindow();
                        progress.Show();
                        using (var download = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                        {
                            download.CancelAfter(TimeSpan.FromMinutes(3));
                            await Download(client, release, stage, download.Token);
                        }
                        release.VerifyArchive(Path.Combine(stage, release.packageName));
                        File.WriteAllText(Path.Combine(stage, Manifest), DataJson.Write(release));
                        var helper = Path.Combine(stage, "Apply.exe");
                        File.Copy(Path.Combine(directory, Executable), helper);
                        File.Copy(Path.Combine(directory, Executable + ".config"), helper + ".config");
                        var start = new ProcessStartInfo(helper, "--apply-update " + Process.GetCurrentProcess().Id)
                        {
                            UseShellExecute = false, WorkingDirectory = stage, WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (var process = Process.Start(start))
                            if (process == null) throw new IOException("Не удалось запустить обновление.");
                        stage = null;
                        owner.Close();
                    }
                }
            }
            catch (Exception error)
            {
                if (feedback != null && !lifetime.IsCancellationRequested)
                    feedback(stage == null ? "Не удалось проверить обновления. Попробуйте позже." : "Не удалось установить обновление.");
                if (stage != null && !lifetime.IsCancellationRequested)
                    BrandDialog.Notify(owner, "Обновление R VPN", "Обновление не установилось. Попробуйте позже.");
                Debug.WriteLine(error.GetType().Name);
            }
            finally
            {
                if (progress != null && progress.IsVisible) progress.Close();
                if (stage != null) Clean(directory, stage);
                busy = false;
            }
        }

        private bool Confirm(string version)
        {
            return BrandDialog.Confirm(owner, "Обновление R VPN",
                "Вышла версия " + version + ". Приложение скачает обновление и перезапустится.", "Обновить", "Позже");
        }

        private Window ProgressWindow()
        {
            return BrandDialog.Progress(owner, "Обновление R VPN", "Скачиваем новую версию…");
        }

        private async Task Download(HttpClient client, ReleaseManifest release, string stage, CancellationToken cancel)
        {
            using (var response = await client.GetAsync(new Uri(service, "build/" + release.packageName),
                HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > PackageLimit) throw new InvalidDataException();
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = new FileStream(Path.Combine(stage, release.packageName), FileMode.CreateNew, FileAccess.Write))
                {
                    var buffer = new byte[65536];
                    long total = 0;
                    int size;
                    while ((size = await input.ReadAsync(buffer, 0, buffer.Length, cancel)) != 0)
                    {
                        total += size;
                        if (total > PackageLimit) throw new InvalidDataException();
                        await output.WriteAsync(buffer, 0, size, cancel);
                    }
                }
            }
        }

        internal static int Apply(string stage, int parentId)
        {
            var directory = Directory.GetParent(stage.TrimEnd(Path.DirectorySeparatorChar)).FullName;
            if (!ValidStage(directory, stage) || !File.Exists(Path.Combine(directory, Executable))) return 1;
            var changed = new List<string>();
            var backup = Path.Combine(stage, "backup");
            var applied = false;
            try
            {
                var release = DataJson.Read<ReleaseManifest>(File.ReadAllText(Path.Combine(stage, Manifest)));
                if (release.Validate() <= Assembly.GetExecutingAssembly().GetName().Version)
                    throw new InvalidDataException();
                var archive = Path.Combine(stage, release.packageName);
                release.VerifyArchive(archive);
                var payload = Path.Combine(stage, "payload");
                Extract(archive, payload);
                Version packaged;
                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(Path.Combine(payload, Executable)).FileVersion, out packaged)
                    || packaged != release.Validate())
                    throw new InvalidDataException("Версия программы в архиве отличается от заявленной.");
                WaitForParent(parentId);
                foreach (var relative in Files)
                {
                    var target = Path.Combine(directory, relative);
                    var source = Path.Combine(payload, relative);
                    var original = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    Directory.CreateDirectory(Path.GetDirectoryName(original));
                    if (File.Exists(target)) File.Replace(source, target, original, true);
                    else File.Move(source, target);
                    changed.Add(relative);
                }
                applied = true;
            }
            catch (Exception)
            {
                try
                {
                    foreach (var relative in Enumerable.Reverse(changed))
                    {
                        var target = Path.Combine(directory, relative);
                        var original = Path.Combine(backup, relative);
                        if (File.Exists(original)) File.Replace(original, target, null, true);
                        else File.Delete(target);
                    }
                }
                catch (Exception)
                {
                    BrandDialog.Notify(null, "Обновление R VPN", "Не удалось восстановить файлы. Скачайте новую версию R VPN.");
                    return 1;
                }
            }
            try
            {
                var start = new ProcessStartInfo(Path.Combine(directory, Executable),
                    "--clean-update " + Path.GetFileName(stage.TrimEnd(Path.DirectorySeparatorChar)))
                {
                    UseShellExecute = false, WorkingDirectory = directory
                };
                if (!applied) BrandDialog.Notify(null, "Обновление R VPN", "Обновление не установилось. Открываю предыдущую версию.");
                using (Process.Start(start)) { }
            }
            catch (Exception)
            {
                BrandDialog.Notify(null, "Обновление R VPN", "Запустите R VPN из папки приложения.");
                return 1;
            }
            return applied ? 0 : 1;
        }

        private static void WaitForParent(int parentId)
        {
            try
            {
                using (var parent = Process.GetProcessById(parentId))
                    if (!parent.WaitForExit(30000)) throw new IOException("Программа не закрылась.");
            }
            catch (ArgumentException) { }
        }

        internal static void Extract(string archive, string payload)
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                long size = 0;
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/", StringComparison.Ordinal) || name == "R VPN/RVPN.Settings.json") continue;
                    if (!name.StartsWith("R VPN/", StringComparison.Ordinal)) throw new InvalidDataException();
                    var relative = name.Substring("R VPN/".Length);
                    size += entry.Length;
                    if (size > PackageLimit || !Files.Contains(relative) || !paths.Add(relative))
                        throw new InvalidDataException();
                    var target = Path.Combine(payload, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target);
                }
                if (paths.Count != Files.Length) throw new InvalidDataException();
            }
        }

        internal static void Clean(string directory, string stage)
        {
            if (!ValidStage(directory, stage)) return;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    var folder = new DirectoryInfo(stage);
                    if (folder.Exists && SafeTree(folder)) folder.Delete(true);
                    return;
                }
                catch (IOException) { Thread.Sleep(400); }
                catch (UnauthorizedAccessException) { Thread.Sleep(400); }
            }
        }

        private static bool SafeTree(DirectoryInfo folder)
        {
            return (folder.Attributes & FileAttributes.ReparsePoint) == 0 && folder.EnumerateDirectories().All(SafeTree);
        }

        private static bool ValidStage(string directory, string stage)
        {
            var absolute = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar);
            return String.Equals(Path.GetDirectoryName(absolute),
                       Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                   Regex.IsMatch(Path.GetFileName(absolute), @"^\.rvpn-update-[0-9a-f]{32}$");
        }
    }
}
