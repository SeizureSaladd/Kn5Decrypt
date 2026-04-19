using System.Text;

namespace Kn5Decrypt;

internal static class AcdUnpacker
{
    public static void Run(string acdPathRaw, string outDirRaw)
    {
        var acdPath = Path.GetFullPath(acdPathRaw);
        var outDir = Path.GetFullPath(outDirRaw);
        var keySource = GetKeySource(acdPath);
        var keyString = CreateKey(keySource);

        Ui.Banner("data.acd unpack", "Reading the archive, deriving the key, and writing decrypted files.");
        Ui.Detail($"Source archive: {acdPath}");
        Ui.Detail($"Output folder: {outDir}");
        Ui.Detail($"Key source: {keySource}");
        Ui.Detail($"Derived key: {keyString}");

        Directory.CreateDirectory(outDir);
        var fileBytes = File.ReadAllBytes(acdPath);

        using var ms = new MemoryStream(fileBytes);
        using var reader = new BinaryReader(ms);

        var first = reader.ReadInt32();
        if (first == -1111) reader.ReadInt32();
        else ms.Seek(0, SeekOrigin.Begin);

        var count = 0;
        while (ms.Position < ms.Length)
        {
            var nameLen = reader.ReadInt32();
            if (nameLen is <= 0 or > 512) break;
            var name = Encoding.ASCII.GetString(reader.ReadBytes(nameLen));

            var dataLen = reader.ReadInt32();
            var packed = new byte[dataLen];
            for (var i = 0; i < dataLen; i++)
            {
                packed[i] = reader.ReadByte();
                reader.ReadBytes(3);
            }

            Decrypt(packed, keyString);

            var outPath = Path.Combine(outDir, name);
            File.WriteAllBytes(outPath, packed);
            Ui.Detail($"Extracted {name} ({dataLen:N0} bytes).");
            count++;
        }

        Ui.Success($"Extracted {count} {(count == 1 ? "file" : "files")} to {outDir}");
    }

    private static string GetKeySource(string acdPath)
    {
        var filename = Path.GetFileName(acdPath);
        return filename.StartsWith("data", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Path.GetDirectoryName(acdPath)!) : filename;
    }

    private static byte IntToByte(int value) => (byte)((value % 256 + 256) % 256);

    private static string CreateKey(string s)
    {
        s = s.ToLower();

        var b1 = IntToByte(s.Aggregate(0, (current, t) => current + t));

        var n2 = 0;
        for (var i = 0; i < s.Length - 1; i += 2)
            n2 = n2 * s[i] - s[i + 1];
        var b2 = IntToByte(n2);

        var n3 = 0;
        for (var j = 1; j < s.Length - 3; j += 3)
        {
            n3 *= s[j];
            n3 /= s[j + 1] + 27;
            n3 += -27 - s[j - 1];
        }
        var b3 = IntToByte(n3);

        var n4 = 5763;
        for (var k = 1; k < s.Length; k++) n4 -= s[k];
        var b4 = IntToByte(n4);

        var n5 = 66;
        for (var l = 1; l < s.Length - 4; l += 4)
            n5 = (s[l] + 15) * n5 * (s[l - 1] + 15) + 22;
        var b5 = IntToByte(n5);

        var n6 = 101;
        for (var m = 0; m < s.Length - 2; m += 2) n6 -= s[m];
        var b6 = IntToByte(n6);

        var n7 = 171;
        for (var n = 0; n < s.Length - 2; n += 2) n7 %= s[n];
        var b7 = IntToByte(n7);

        var n8 = 171;
        for (var i = 0; i < s.Length - 1; i++)
            n8 = n8 / s[i] + s[i + 1];
        var b8 = IntToByte(n8);

        return string.Join("-", b1, b2, b3, b4, b5, b6, b7, b8);
    }

    private static void Decrypt(byte[] data, string key)
    {
        var keyLen = key.Length - 1;
        if (keyLen < 0) return;

        var ki = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var val = data[i] - key[ki];
            data[i] = (byte)(val < 0 ? val + 256 : val);
            ki = (ki == keyLen) ? 0 : ki + 1;
        }
    }
}
