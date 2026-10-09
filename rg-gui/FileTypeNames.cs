using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace rg_gui
{
    /// <summary>
    /// Looks up the file type description Windows Explorer shows, e.g. "Text Document" for ".txt".
    /// </summary>
    public static class FileTypeNames
    {
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint SHGFI_TYPENAME = 0x400;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static string Get(string extension)
        {
            return Cache.GetOrAdd(extension, Lookup);
        }

        private static string Lookup(string extension)
        {
            try
            {
                // With SHGFI_USEFILEATTRIBUTES only the name is used, so the file doesn't need to exist.
                var info = new SHFILEINFO();
                if (SHGetFileInfo("file" + extension, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES) != IntPtr.Zero &&
                    !string.IsNullOrEmpty(info.szTypeName))
                {
                    return info.szTypeName;
                }
            }
            catch (Exception)
            {
            }

            return extension.Length > 1 ? $"{extension[1..].ToUpperInvariant()} File" : "File";
        }
    }
}
