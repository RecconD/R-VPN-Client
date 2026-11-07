using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RVpn
{
    internal static class NativeEngineHost
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ProcessLimit;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobLimits
        {
            public BasicLimits Basic;
            public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref JobLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint type, uint group);
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        public static int Main(string[] args)
        {
            int parentId;
            Guid marker;
            if (args.Length != 2 || !Int32.TryParse(args[0], out parentId) || !Guid.TryParseExact(args[1], "N", out marker)) return 2;
            var ownConsole = GetConsoleWindow();
            if (ownConsole != IntPtr.Zero) ShowWindow(ownConsole, 0);
            Console.CancelKeyPress += (sender, eventArgs) => eventArgs.Cancel = true;
            var directory = AppDomain.CurrentDomain.BaseDirectory;
            if (!CoreFiles.Check(directory)) return 3;

            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return 4;
            try
            {
                var limits = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 } };
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(JobLimits)))) return 4;
                using (var stop = EventWaitHandle.OpenExisting(@"Local\RVPN.Stop." + marker.ToString("N")))
                using (var parent = Process.GetProcessById(parentId))
                {
                    var config = ReadConfig();
                    if (config.Length == 0 || parent.HasExited || stop.WaitOne(0)) return 2;
                    using (var engine = Process.Start(new ProcessStartInfo(Path.Combine(directory, "sing-box.exe"), "run -c stdin --disable-color")
                    {
                        WorkingDirectory = directory, UseShellExecute = false, RedirectStandardInput = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    }))
                    {
                        if (engine == null) return 5;
                        if (!AssignProcessToJobObject(job, engine.Handle)) { engine.Kill(); return 4; }
                        engine.OutputDataReceived += (sender, data) => { };
                        engine.ErrorDataReceived += (sender, data) => { };
                        engine.BeginOutputReadLine();
                        engine.BeginErrorReadLine();
                        engine.StandardInput.BaseStream.Write(config, 0, config.Length);
                        Array.Clear(config, 0, config.Length);
                        engine.StandardInput.Close();
                        while (!engine.HasExited && !parent.HasExited && !stop.WaitOne(170)) { }
                        if (engine.HasExited) return engine.ExitCode;
                        GenerateConsoleCtrlEvent(0, 0);
                        if (!engine.WaitForExit(4200)) { engine.Kill(); engine.WaitForExit(1000); }
                        return 0;
                    }
                }
            }
            catch (Exception) { return 5; }
            finally { CloseHandle(job); }
        }

        private static byte[] ReadConfig()
        {
            using (var input = Console.OpenStandardInput())
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[4096];
                int bytes;
                while ((bytes = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + bytes > 1024 * 1024) throw new InvalidDataException();
                    buffer.Write(chunk, 0, bytes);
                }
                Array.Clear(chunk, 0, chunk.Length);
                return buffer.ToArray();
            }
        }
    }
}
