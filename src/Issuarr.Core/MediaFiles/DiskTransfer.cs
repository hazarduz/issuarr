using System.Runtime.InteropServices;

namespace Issuarr.Core.MediaFiles;

public enum TransferMode
{
    Move,
    Copy,
    HardLinkOrCopy,
}

/// <summary>Moves, copies or hardlinks files. Hardlinks let torrents keep seeding without using extra space.</summary>
public static partial class DiskTransfer
{
    /// <returns>What actually happened (a hardlink falls back to a copy across drives).</returns>
    public static TransferMode Transfer(string source, string destination, TransferMode mode, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (overwrite && File.Exists(destination))
        {
            File.Delete(destination);
        }

        switch (mode)
        {
            case TransferMode.Move:
                try
                {
                    File.Move(source, destination, overwrite);
                }
                catch (IOException)
                {
                    // Different file systems: copy, then remove the original.
                    File.Copy(source, destination, overwrite);
                    File.Delete(source);
                }

                return TransferMode.Move;

            case TransferMode.HardLinkOrCopy when TryHardLink(source, destination):
                return TransferMode.HardLinkOrCopy;

            default:
                File.Copy(source, destination, overwrite);
                return TransferMode.Copy;
        }
    }

    public static bool TryHardLink(string source, string destination)
    {
        try
        {
            return OperatingSystem.IsWindows() ? CreateHardLinkW(destination, source, IntPtr.Zero) : link(source, destination) == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int link(string oldpath, string newpath);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
