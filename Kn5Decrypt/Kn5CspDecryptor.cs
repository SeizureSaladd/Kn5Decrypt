using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Kn5Decrypt;

internal static class Kn5CspDecrypter
{
    private const string Magic = "__AC_SHADERS_PATCH_KN5ENC_v1__";
    private const int MagicLen = 30;
    private const int TailLen = MagicLen + 8;

    public static void Run(string kn5PathRaw, string? outDirRaw)
    {
        var kn5Path = Path.GetFullPath(kn5PathRaw);
        var displayRoot = Path.GetDirectoryName(kn5Path)!;
        var outDir = outDirRaw != null
            ? Path.GetFullPath(outDirRaw)
            : Path.Combine(Path.GetDirectoryName(kn5Path)!, Path.GetFileNameWithoutExtension(kn5Path) + "_decrypted");

        Ui.Banner("CSP KN5 decrypt", "Recovering textures, vertex masks, and a plaintext KN5 where possible.");
        Ui.Detail($"Input KN5: {Path.GetRelativePath(displayRoot, kn5Path)}");
        Ui.Detail($"Output folder: {Path.GetRelativePath(displayRoot, outDir)}");

        var data = File.ReadAllBytes(kn5Path);
        if (!DetectEnvelope(data, out var sidecarOffset, out var encVersion))
        {
            Ui.Warn("No CSP v1 envelope was found. The file is already plain KN5 data or uses a different protection scheme. Try option 3 if you want to be able to unpack LODs.");
            return;
        }
        Ui.Detail($"Envelope detected at sidecar offset 0x{sidecarOffset:X8} with encVersion {encVersion}.");
        if (encVersion > 3)
        {
            Ui.Error($"encVersion {encVersion} is not supported by this tool.");
            return;
        }
        
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "textures"));
        Directory.CreateDirectory(Path.Combine(outDir, "vertex_masks"));

        var sidecar = ReadSidecar(data, sidecarOffset, data.Length - TailLen);
        Ui.Detail($"Loaded {sidecar.Count} sidecar {(sidecar.Count == 1 ? "entry" : "entries")}.");

        var folderName = Path.GetFileName(Path.GetDirectoryName(kn5Path)!);
        var baseSalt = "car model/" + folderName;
        var salts = new List<string> { baseSalt };
        var acdPath = Path.Combine(Path.GetDirectoryName(kn5Path)!, "data.acd");
        if (File.Exists(acdPath))
        {
            var acdB64 = ToBase64NoPad(SHA256.HashData(File.ReadAllBytes(acdPath)));
            Ui.Detail($"Found sibling data.acd checksum: {acdB64}");
            salts.Insert(0, baseSalt + ":" + acdB64);
        }

        var activeSalt = ResolveSalt(sidecar, salts);
        if (activeSalt == null)
        {
            Ui.Error("Could not resolve a working per-car salt from the KN5 and data.acd pair.");
            foreach (var s in salts) Ui.Detail($"Tried salt: {s}");
            return;
        }
        Ui.Success($"Resolved car salt: {activeSalt}");

        var decryptedTextures = DecryptTextures(sidecar, activeSalt, Path.Combine(outDir, "textures"), displayRoot);
        var decryptedMasks = DecryptVertexMasks(sidecar, activeSalt, encVersion, Path.Combine(outDir, "vertex_masks"), displayRoot);

        if (encVersion == 1)
        {
            var bodyBytes = data[..(int)sidecarOffset];
            var parser = new Kn5BodyDecryptor(bodyBytes);
            try
            {
                parser.Parse();
                Ui.Info($"Parsed the KN5 body: {parser.Textures.Count} texture slots and {parser.Meshes.Count} meshes.");
                int patched = 0, missing = 0;
                foreach (var mesh in parser.Meshes)
                {
                    if (!decryptedMasks.TryGetValue(mesh.Name, out var mask)) { missing++; continue; }
                    Kn5BodyDecryptor.ApplyMaskV1(bodyBytes, mesh, mask);
                    patched++;
                }
                Ui.Detail($"Applied {patched} vertex masks; {missing} meshes had no matching mask.");
                var decryptedKn5 = parser.EmitWithTextures(decryptedTextures);
                var outKn5 = Path.Combine(outDir, Path.GetFileNameWithoutExtension(kn5Path) + "_decrypted.kn5");
                File.WriteAllBytes(outKn5, decryptedKn5);
                Ui.Success($"Wrote rebuilt KN5 to {Path.GetRelativePath(displayRoot, outKn5)} ({decryptedKn5.Length:N0} bytes).");
            }
            catch (Exception ex)
            {
                Ui.Warn($"Could not rebuild the full KN5 body: {ex.Message}");
                var bodyPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(kn5Path) + "_body.kn5");
                File.WriteAllBytes(bodyPath, bodyBytes);
                Ui.Success($"Wrote the decrypted body-only file to {Path.GetRelativePath(displayRoot, bodyPath)}. KN5 will have incorrect geometry.");
            }
        }
        else
        {
            var bodyPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(kn5Path) + "_body.kn5");
            File.WriteAllBytes(bodyPath, data[..(int)sidecarOffset]);
            Ui.Warn($"Full KN5 rebuilding is not implemented for encVersion {encVersion}.");
            Ui.Success($"Wrote the decrypted body-only file to {Path.GetRelativePath(displayRoot, bodyPath)}");
        }

        Ui.Success("CSP decryption pass finished.");
    }

    private static bool DetectEnvelope(byte[] data, out uint sidecarOffset, out uint encVersion)
    {
        sidecarOffset = 0;
        encVersion = 0;
        if (data.Length < TailLen + 32) return false;
        var tailMagic = Encoding.ASCII.GetString(data, data.Length - TailLen, MagicLen);
        if (tailMagic != Magic) return false;
        sidecarOffset = BitConverter.ToUInt32(data, data.Length - 8);
        encVersion = BitConverter.ToUInt32(data, data.Length - 4);
        return true;
    }

    private static Dictionary<string, byte[]> ReadSidecar(byte[] data, long start, long end)
    {
        var dict = new Dictionary<string, byte[]>();
        var pos = start;
        while (pos < end)
        {
            var nameLen = BitConverter.ToInt32(data, (int)pos); pos += 4;
            var name = Encoding.UTF8.GetString(data, (int)pos, nameLen); pos += nameLen;
            var dataLen = BitConverter.ToInt32(data, (int)pos); pos += 4;
            var dataPos = pos;
            if (name.Length > 6 && name[3] == '.' && name[^2] == '.')
            {
                var payload = new byte[dataLen];
                Buffer.BlockCopy(data, (int)dataPos, payload, 0, dataLen);
                dict[name] = payload;
            }
            pos = dataPos + dataLen;
        }
        return dict;
    }

    private static byte[] AesDecrypt(byte[] ciphertext, byte[] iv, uint kRaw, string carKey)
    {
        var id = kRaw ^ BitConverter.ToUInt32(iv, 0);
        if (!CspKeyTable.Keys.TryGetValue(id, out var rawKeyMat))
            throw new InvalidOperationException($"Key id 0x{id:X8} not in static store");
        var source = new byte[rawKeyMat.Length + 4];
        Buffer.BlockCopy(BitConverter.GetBytes(id), 0, source, 0, 4);
        Buffer.BlockCopy(rawKeyMat, 0, source, 4, rawKeyMat.Length);
        for (var i = 0; i < carKey.Length; i++)
            source[4 + i % (source.Length - 4)] ^= (byte)carKey[i];

        var aesKey = new byte[source.Length - 4];
        Buffer.BlockCopy(source, 4, aesKey, 0, aesKey.Length);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = aesKey;
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        using var output = new MemoryStream();
        using (var cs = new CryptoStream(output, dec, CryptoStreamMode.Write))
            cs.Write(ciphertext, 0, ciphertext.Length);
        return output.ToArray();
    }

    private static void XorInPlace(byte[] dst, byte[] src)
    {
        for (var i = 0; i < dst.Length; i++)
            dst[i] ^= src[i % src.Length];
    }

    private static Dictionary<string, byte[]> DecryptTextures(Dictionary<string, byte[]> sidecar, string carKey, string outDir, string displayRoot)
    {
        var result = new Dictionary<string, byte[]>();
        var texNames = new HashSet<string>();
        foreach (var k in sidecar.Keys.Where(k => k.StartsWith("tex.") && k.EndsWith(".k")))
            texNames.Add(k.Substring(4, k.Length - 6));
        Ui.Info($"Decrypting {texNames.Count} texture {(texNames.Count == 1 ? "entry" : "entries")}.");

        foreach (var name in texNames)
        {
            if (!sidecar.TryGetValue($"tex.{name}.k", out var kBytes)) continue;
            if (!sidecar.TryGetValue($"tex.{name}.i", out var iv)) continue;
            if (!sidecar.TryGetValue($"tex.{name}.x", out var cipher)) continue;
            if (!sidecar.TryGetValue($"tex.{name}.d", out var dBlob))
            {
                Ui.Warn($"Texture '{name}' is missing the .d payload, so it was skipped.");
                continue;
            }
            var kRaw = BitConverter.ToUInt32(kBytes, 0);
            byte[] aesOut;
            try { aesOut = AesDecrypt(cipher, iv, kRaw, carKey); }
            catch (Exception ex)
            {
                Ui.Warn($"Texture '{name}' could not be AES-decrypted: {ex.Message}");
                continue;
            }

            var dCopy = (byte[])dBlob.Clone();
            XorInPlace(dCopy, aesOut);

            var dds = TryZlibDecompress(dCopy);
            if (dds == null)
            {
                var rawPath = Path.Combine(outDir, SanitizeFileName(name) + ".xored_raw.bin");
                Ui.Warn($"Texture '{name}' did not inflate cleanly. Saving the XORed payload to {Path.GetRelativePath(displayRoot, rawPath)}");
                File.WriteAllBytes(rawPath, dCopy);
                continue;
            }
            var texturePath = Path.Combine(outDir, SanitizeFileName(name));
            File.WriteAllBytes(texturePath, dds);
            result[name] = dds;
            var marker = dds.Length >= 4 ? Encoding.ASCII.GetString(dds, 0, 4) : "<short>";
            Ui.Detail($"Texture '{name}' -> {Path.GetRelativePath(displayRoot, texturePath)} ({dds.Length:N0} bytes, header '{marker}')");
        }
        return result;
    }

    private static Dictionary<string, byte[]> DecryptVertexMasks(Dictionary<string, byte[]> sidecar, string carKey, uint encVersion, string outDir, string displayRoot)
    {
        var result = new Dictionary<string, byte[]>();
        var names = new HashSet<string>();
        foreach (var k in sidecar.Keys.Where(k => k.StartsWith("ver.") && k.EndsWith(".k")))
            names.Add(k.Substring(4, k.Length - 6));
        Ui.Info($"Decrypting {names.Count} vertex mask {(names.Count == 1 ? "entry" : "entries")}.");

        var manifestPath = Path.Combine(outDir, "manifest.txt");
        using var manifest = new StreamWriter(manifestPath);
        manifest.WriteLine($"encVersion={encVersion}");
        manifest.WriteLine("# name\tmaskLen\tdelta");
        foreach (var name in names)
        {
            if (!sidecar.TryGetValue($"ver.{name}.k", out var kBytes)) continue;
            if (!sidecar.TryGetValue($"ver.{name}.i", out var iv)) continue;
            if (!sidecar.TryGetValue($"ver.{name}.x", out var cipher)) continue;
            var delta = 0.03f;
            if (encVersion != 1)
            {
                if (!sidecar.TryGetValue($"ver.{name}.f", out var f) || f.Length < 4)
                {
                    Ui.Warn($"Vertex mask '{name}' is missing ver.f, so it was skipped.");
                    continue;
                }
                delta = BitConverter.ToSingle(f, 0);
            }
            var kRaw = BitConverter.ToUInt32(kBytes, 0);
            byte[] mask;
            try { mask = AesDecrypt(cipher, iv, kRaw, carKey); }
            catch (Exception ex)
            {
                Ui.Warn($"Vertex mask '{name}' could not be AES-decrypted: {ex.Message}");
                continue;
            }
            var maskPath = Path.Combine(outDir, SanitizeFileName(name) + ".bin");
            File.WriteAllBytes(maskPath, mask);
            result[name] = mask;
            manifest.WriteLine($"{name}\t{mask.Length}\t{delta:G9}");
            Ui.Detail($"Vertex mask '{name}' -> {Path.GetRelativePath(displayRoot, maskPath)} ({mask.Length:N0} bytes).");
        }
        Ui.Detail($"Vertex mask manifest: {Path.GetRelativePath(displayRoot, manifestPath)}");
        return result;
    }

    private static string? ResolveSalt(Dictionary<string, byte[]> sidecar, List<string> candidates)
    {
        foreach (var k in sidecar.Keys)
        {
            if (!k.StartsWith("tex.") || !k.EndsWith(".k")) continue;
            var name = k.Substring(4, k.Length - 6);
            if (!sidecar.ContainsKey($"tex.{name}.i")) continue;
            if (!sidecar.ContainsKey($"tex.{name}.x")) continue;
            if (!sidecar.ContainsKey($"tex.{name}.d")) continue;

            var kBytes = sidecar[$"tex.{name}.k"];
            var iv = sidecar[$"tex.{name}.i"];
            var cipher = sidecar[$"tex.{name}.x"];
            var dBlob = sidecar[$"tex.{name}.d"];
            var kRaw = BitConverter.ToUInt32(kBytes, 0);

            foreach (var salt in candidates)
            {
                try
                {
                    var aesOut = AesDecrypt(cipher, iv, kRaw, salt);
                    var dCopy = (byte[])dBlob.Clone();
                    XorInPlace(dCopy, aesOut);
                    var dds = TryZlibDecompress(dCopy);
                    if (dds is { Length: >= 4 })
                        return salt;
                }
                catch
                {
                    // ignored
                }
            }
            break;
        }
        return null;
    }

    private static byte[]? TryZlibDecompress(byte[] data)
    {
        try
        {
            using var src = new MemoryStream(data);
            using var zl = new ZLibStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream();
            zl.CopyTo(dst);
            return dst.ToArray();
        }
        catch { return null; }
    }

    private static string ToBase64NoPad(byte[] b) => Convert.ToBase64String(b).TrimEnd('=');

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
