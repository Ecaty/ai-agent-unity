using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Ecaty.Agents.Security
{
    public sealed class WindowsSecretStore : ISecretStore
    {
        private readonly string path;
        public WindowsSecretStore(string applicationName)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("This connection currently supports Windows.");
            if (!Regex.IsMatch(applicationName ?? "", "^[A-Za-z][A-Za-z0-9_.-]{0,63}$"))
                throw new ArgumentException("Invalid application name.");
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName, "Agents", "chatgpt.dat");
        }
        public byte[] Load() => File.Exists(path) ? Transform(File.ReadAllBytes(path), false) : null;
        public void Save(byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, Transform(data, true));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static byte[] Transform(byte[] input, bool protect)
        {
            var source = new Blob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(input, 0, source.Data, input.Length);
                bool ok = protect
                    ? CryptProtectData(ref source, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Protected connection storage is unavailable.");
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                for (int i = 0; i < source.Size; i++) Marshal.WriteByte(source.Data, i, 0);
                Marshal.FreeHGlobal(source.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
