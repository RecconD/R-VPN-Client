using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RVpn
{
    internal static class WindowsShell
    {
        private const string AppId = "RProject.RVPN.Desktop";
        private static readonly Guid AppIdFormat = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr other);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropertyValue
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr String;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropertyValue value);
            void SetValue(ref PropertyKey key, ref PropertyValue value);
            void Commit();
        }

        internal static void SetApplicationIdentity()
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
        }

        internal static void UpdateShortcutIdentity(string path)
        {
            object link = null;
            var value = new PropertyValue { Type = 31, String = Marshal.StringToCoTaskMemUni(AppId) };
            try
            {
                link = new ShellLink();
                var file = (IPersistFile)link;
                file.Load(path, 2);
                var store = (IPropertyStore)link;
                var key = new PropertyKey { Format = AppIdFormat, Id = 5 };
                store.SetValue(ref key, ref value);
                store.Commit();
                file.Save(path, true);
            }
            catch (COMException) { }
            finally
            {
                Marshal.FreeCoTaskMem(value.String);
                if (link != null) Marshal.FinalReleaseComObject(link);
            }
            // SHCNE_UPDATEITEM, SHCNF_PATHW.
            SHChangeNotify(0x2000, 5, path, IntPtr.Zero);
        }
    }
}
