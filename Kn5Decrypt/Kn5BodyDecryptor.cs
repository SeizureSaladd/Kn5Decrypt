using System.Text;

namespace Kn5Decrypt;

internal sealed class Kn5BodyDecryptor(byte[] buf)
{
    public sealed class TexEntry
    {
        public int Active;
        public string Name = "";
        public int BodyOffset;
        public int BodyLength;
    }

    public sealed class MeshEntry
    {
        public string Name = "";
        public int VertexBufferOffset;
        public int VertexCount;
        public bool IsSkinned;
        public int Stride => IsSkinned ? 76 : 44;
    }

    private int _pos;

    public List<TexEntry> Textures { get; } = new();
    public List<MeshEntry> Meshes { get; } = new();
    public int TextureSectionStart { get; private set; }
    public int TextureSectionEnd { get; private set; }
    public int BodyEnd { get; private set; }

    public void Parse()
    {
        if (Encoding.ASCII.GetString(buf, 0, 6) != "sc6969")
            throw new InvalidDataException("not a KN5 body (missing sc6969 magic)");
        _pos = 6;
        var version = ReadInt32();
        if (version > 5) ReadInt32();

        TextureSectionStart = _pos;
        var texCount = ReadInt32();
        for (var i = 0; i < texCount; i++)
        {
            var t = new TexEntry { Active = ReadInt32() };
            if (t.Active == 0) { Textures.Add(t); continue; }
            t.Name = ReadString();
            t.BodyLength = (int)ReadUInt32();
            t.BodyOffset = _pos;
            _pos += t.BodyLength;
            Textures.Add(t);
        }
        TextureSectionEnd = _pos;

        var matCount = ReadInt32();
        for (var i = 0; i < matCount; i++) SkipMaterial();

        ReadNode();
        BodyEnd = _pos;
    }

    private void SkipMaterial()
    {
        SkipString();   // ShaderName
        SkipString();   // MaterialName
        _pos += 6;      // BlendMode(byte) + AlphaTested(byte) + DepthMode(int)
        var propCount = ReadInt32();
        for (var i = 0; i < propCount; i++) { SkipString(); _pos += 40; }
        var mapCount = ReadInt32();
        for (var i = 0; i < mapCount; i++) { SkipString(); _pos += 4; SkipString(); }
    }

    private void ReadNode()
    {
        var nodeClass = ReadInt32();
        var name = ReadString();
        var childCount = ReadInt32();
        _pos += 1; // Active bool
        switch (nodeClass)
        {
            case 0:
                break;                // treated as Base, no transform read
            case 1:
                _pos += 64;           // Mat4x4
                break;
            case 2:
                _pos += 3;            // CastShadows, IsVisible, IsTransparent
                var vertCount = (int)ReadUInt32();
                Meshes.Add(new MeshEntry { Name = name, VertexBufferOffset = _pos, VertexCount = vertCount, IsSkinned = false });
                _pos += vertCount * 44;
                var idxCount = (int)ReadUInt32();
                _pos += idxCount * 2;
                _pos += 4 + 4;        // MaterialId + Layer
                _pos += 4 + 4;        // LodIn + LodOut
                _pos += 12;           // BoundingSphereCenter
                _pos += 4;            // BoundingSphereRadius
                _pos += 1;            // IsRenderable
                break;
            case 3:
                _pos += 3;
                var boneCount = (int)ReadUInt32();
                for (var i = 0; i < boneCount; i++) { SkipString(); _pos += 64; }
                var sVertCount = (int)ReadUInt32();
                Meshes.Add(new MeshEntry { Name = name, VertexBufferOffset = _pos, VertexCount = sVertCount, IsSkinned = true });
                _pos += sVertCount * 76;   // 44 vertex + 16 weights + 16 indices
                var sIdxCount = (int)ReadUInt32();
                _pos += sIdxCount * 2;
                _pos += 4 + 4;        // MaterialId + Layer
                _pos += 8;            // Unknown tail preserved from the original layout
                break;
            default:
                throw new InvalidDataException($"unknown node class {nodeClass} at offset 0x{_pos - 4:X}");
        }
        for (var i = 0; i < childCount; i++) ReadNode();
    }

    public static void ApplyMaskV1(byte[] body, MeshEntry mesh, byte[] mask)
    {
        const float D = 0.03f;
        var stride = mesh.Stride;
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var b = mask[i % mask.Length];
            var o = mesh.VertexBufferOffset + i * stride;
            AddF(body, o + 0, (b & 1)  != 0 ? -D : +D);
            AddF(body, o + 4, (b & 2)  != 0 ? -D : +D);
            AddF(body, o + 8, (b & 4)  != 0 ? -D : +D);
            if ((b & 8)   != 0) NegF(body, o + 12);
            if ((b & 16)  != 0) NegF(body, o + 16);
            if ((b & 32)  != 0) NegF(body, o + 20);
            if ((b & 64)  != 0) InvF(body, o + 24);
            if ((b & 128) != 0) InvF(body, o + 28);
        }
    }

    public byte[] EmitWithTextures(Dictionary<string, byte[]> decryptedTextures)
    {
        var ms = new MemoryStream();
        ms.Write(buf, 0, TextureSectionStart);
        WriteInt32(ms, Textures.Count);
        foreach (var t in Textures)
        {
            WriteInt32(ms, t.Active);
            if (t.Active == 0) continue;
            WriteString(ms, t.Name);
            var body = decryptedTextures.TryGetValue(t.Name, out var dds)
                ? dds
                : SliceCopy(buf, t.BodyOffset, t.BodyLength);
            WriteUInt32(ms, (uint)body.Length);
            ms.Write(body, 0, body.Length);
        }
        ms.Write(buf, TextureSectionEnd, BodyEnd - TextureSectionEnd);
        return ms.ToArray();
    }

    private int ReadInt32() { var v = BitConverter.ToInt32(buf, _pos); _pos += 4; return v; }
    private uint ReadUInt32() { var v = BitConverter.ToUInt32(buf, _pos); _pos += 4; return v; }
    private string ReadString()
    {
        var n = (int)ReadUInt32();
        var s = Encoding.UTF8.GetString(buf, _pos, n);
        _pos += n;
        return s;
    }
    private void SkipString() { _pos += 4 + (int)BitConverter.ToUInt32(buf, _pos); }

    private static void AddF(byte[] b, int o, float d)
    {
        var v = BitConverter.ToSingle(b, o) + d;
        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);
    }
    private static void NegF(byte[] b, int o)
    {
        var v = -BitConverter.ToSingle(b, o);
        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);
    }
    private static void InvF(byte[] b, int o)
    {
        var v = 1f - BitConverter.ToSingle(b, o);
        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);
    }
    private static byte[] SliceCopy(byte[] src, int offset, int length)
    {
        var r = new byte[length];
        Buffer.BlockCopy(src, offset, r, 0, length);
        return r;
    }
    private static void WriteInt32(MemoryStream ms, int v) => ms.Write(BitConverter.GetBytes(v), 0, 4);
    private static void WriteUInt32(MemoryStream ms, uint v) => ms.Write(BitConverter.GetBytes(v), 0, 4);
    private static void WriteString(MemoryStream ms, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        WriteUInt32(ms, (uint)b.Length);
        ms.Write(b, 0, b.Length);
    }
}
