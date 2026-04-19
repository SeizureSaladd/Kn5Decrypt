// Removes kn5 unpack protection. Re-enables the "Unpack LODs" button in Custom Showroom.

using System.Text;

namespace Kn5Decrypt;

internal static class Kn5Protection
{
    public static void Run(string kn5PathRaw)
    {
        var path = Path.GetFullPath(kn5PathRaw);
        var backup = path + ".bak";

        Ui.Banner("KN5 unprotect", "Backing up the file, checking for the dummy entry, and patching in place when needed.");
        Ui.Detail($"Target KN5: {path}");

        File.Copy(path, backup, overwrite: true);
        Ui.Success($"Backup written to {backup}");

        var data = File.ReadAllBytes(path);
        var offset = 0;

        if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 6) != "sc6969")
            throw new InvalidDataException("not a KN5 file (missing sc6969 magic)");
        offset += 6;

        var version = BitConverter.ToInt32(data, offset);
        if (version != 6) throw new InvalidDataException($"Unexpected version {version} (expected 6)");
        offset += 4;

        offset += 4; // v6 reserved integer

        var texCountOffset = offset;
        var texCount = BitConverter.ToInt32(data, texCountOffset);
        Ui.Detail($"Texture table reports {texCount} {(texCount == 1 ? "entry" : "entries")} before patching.");

        var protectionOffset = texCountOffset + 4;
        var protectionValue = BitConverter.ToInt32(data, protectionOffset);
        if (protectionValue != 0)
        {
            Ui.Warn("This KN5 does not appear to be protected. No changes were written.");
            return;
        }
        Ui.Info("Protection marker found. Rewriting the texture table.");

        Buffer.BlockCopy(BitConverter.GetBytes(texCount - 1), 0, data, texCountOffset, 4);

        var patched = new byte[data.Length - 4];
        Buffer.BlockCopy(data, 0, patched, 0, protectionOffset);
        Buffer.BlockCopy(data, protectionOffset + 4, patched, protectionOffset, data.Length - protectionOffset - 4);

        File.WriteAllBytes(path, patched);
        Ui.Success($"Protection removed. Texture count is now {texCount - 1}.");
    }
}
