using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FFXIISaveEditor
{
    // "SAFH" | u32 decoded size | u32 r | RC4(zlib(data)), key = MD5 of the title rotated left by r, first 31 chars.
    static class Codec
    {
        static readonly byte[] Title = Encoding.ASCII.GetBytes("FINAL FANTASY XII THE ZODIAC AGE");

        public static byte[] Decode(byte[] blob, out int r)
        {
            if (blob.Length < 14 || blob[0] != 'S' || blob[1] != 'A' || blob[2] != 'F' || blob[3] != 'H')
                throw new InvalidDataException("Not an FFXII save file (missing SAFH header).");
            int size = BitConverter.ToInt32(blob, 4);
            r = BitConverter.ToInt32(blob, 8);
            if (r < 0 || r >= 32 || size <= 0 || size > 16 * 1024 * 1024)
                throw new InvalidDataException("Unexpected save header.");
            byte[] z = new byte[blob.Length - 12];
            Array.Copy(blob, 12, z, 0, z.Length);
            Rc4(Key(r), z);
            if (z.Length < 6 || z[0] != 0x78)
                throw new InvalidDataException("Decryption failed (no zlib header).");

            byte[] plain = new byte[size];
            using (var ds = new DeflateStream(new MemoryStream(z, 2, z.Length - 6), CompressionMode.Decompress))
            {
                int got = 0, n;
                while (got < size && (n = ds.Read(plain, got, size - got)) > 0) got += n;
                if (got != size || ds.ReadByte() != -1)
                    throw new InvalidDataException("Decompressed size does not match the header.");
            }
            uint adler = (uint)(z[z.Length - 4] << 24 | z[z.Length - 3] << 16 | z[z.Length - 2] << 8 | z[z.Length - 1]);
            if (adler != Adler32(plain))
                throw new InvalidDataException("zlib checksum mismatch.");
            return plain;
        }

        public static byte[] Encode(byte[] plain, int r)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0xDA);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
                ds.Write(plain, 0, plain.Length);
            uint adler = Adler32(plain);
            ms.WriteByte((byte)(adler >> 24));
            ms.WriteByte((byte)(adler >> 16));
            ms.WriteByte((byte)(adler >> 8));
            ms.WriteByte((byte)adler);
            byte[] z = ms.ToArray();
            Rc4(Key(r), z);

            byte[] blob = new byte[12 + z.Length];
            blob[0] = (byte)'S'; blob[1] = (byte)'A'; blob[2] = (byte)'F'; blob[3] = (byte)'H';
            BitConverter.GetBytes(plain.Length).CopyTo(blob, 4);
            BitConverter.GetBytes(r).CopyTo(blob, 8);
            z.CopyTo(blob, 12);
            return blob;
        }

        static byte[] Key(int r)
        {
            byte[] rotated = new byte[31];
            for (int i = 0; i < 31; i++) rotated[i] = Title[(i + r) % Title.Length];
            using (var md5 = MD5.Create()) return md5.ComputeHash(rotated);
        }

        static void Rc4(byte[] key, byte[] data)
        {
            byte[] s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
            }
            for (int n = 0, i = 0, j = 0; n < data.Length; n++)
            {
                i = (i + 1) & 0xFF;
                j = (j + s[i]) & 0xFF;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
                data[n] ^= s[(s[i] + s[j]) & 0xFF];
            }
        }

        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return b << 16 | a;
        }

        static uint[] crcTable;

        public static uint Crc32(byte[] data, int start, int end)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                crcTable = t;
            }
            uint crc = 0xFFFFFFFF;
            for (int i = start; i < end; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }
}
