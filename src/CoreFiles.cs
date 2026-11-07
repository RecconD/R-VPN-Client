using System;
using System.IO;
using System.Security.Cryptography;

namespace RVpn
{
    internal static class CoreFiles
    {
        internal const string SingBoxHash = "8F30682EE198855440D2A7FE070D5D3653793B2D01BB8C85EEA88F93BBE7C8B9";
        internal const string WintunHash = "E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE";

        internal static bool Check(string directory)
        {
            return Matches(Path.Combine(directory, "sing-box.exe"), SingBoxHash) &&
                   Matches(Path.Combine(directory, "wintun.dll"), WintunHash);
        }

        private static bool Matches(string path, string expected)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var sha = SHA256.Create())
                {
                    return String.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""),
                        expected, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
