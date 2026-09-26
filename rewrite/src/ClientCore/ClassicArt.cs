using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// The Trilogy client's archives (.s3d, .pak: "PFS"): a directory of files, each stored as zlib
    /// blocks, with the names in a last file (CRC 0x61580AC9). The interface art is in bmpwad*.s3d.
    /// </summary>
    public static class PfsArchive
    {
        private const uint NamesCrc = 0x61580AC9;

        /// <summary>Every file of the archive by lower-case name.</summary>
        public static Dictionary<string, byte[]> Read(byte[] archive)
        {
            int dirOffset = BitConverter.ToInt32(archive, 0);
            if (Encoding.ASCII.GetString(archive, 4, 4) != "PFS ")
                throw new InvalidDataException("not a PFS archive");
            int count = BitConverter.ToInt32(archive, dirOffset);
            var entries = new List<(uint Crc, int Offset, int Size)>();
            for (int i = 0; i < count; i++)
            {
                int p = dirOffset + 4 + i * 12;
                entries.Add((BitConverter.ToUInt32(archive, p), BitConverter.ToInt32(archive, p + 4), BitConverter.ToInt32(archive, p + 8)));
            }
            entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            var blobs = new List<byte[]>();
            List<string>? names = null;
            foreach (var (crc, offset, size) in entries)
            {
                var data = Inflate(archive, offset, size);
                if (crc == NamesCrc)
                {
                    names = new List<string>();
                    int n = BitConverter.ToInt32(data, 0), p = 4;
                    for (int i = 0; i < n; i++)
                    {
                        int length = BitConverter.ToInt32(data, p);
                        names.Add(Encoding.GetEncoding("ISO-8859-1").GetString(data, p + 4, Math.Max(0, length - 1)).ToLowerInvariant());
                        p += 4 + length;
                    }
                }
                else
                    blobs.Add(data);
            }
            var files = new Dictionary<string, byte[]>();
            for (int i = 0; names != null && i < names.Count && i < blobs.Count; i++)
                files[names[i]] = blobs[i];
            return files;
        }

        private static byte[] Inflate(byte[] archive, int offset, int size)
        {
            var output = new MemoryStream(size);
            int p = offset;
            while (output.Length < size)
            {
                int deflated = BitConverter.ToInt32(archive, p);
                p += 8; // deflated length, inflated length
                // zlib: a 2-byte header, then raw deflate.
                using (var z = new DeflateStream(new MemoryStream(archive, p + 2, deflated - 2), CompressionMode.Decompress))
                    z.CopyTo(output);
                p += deflated;
            }
            return output.ToArray();
        }
    }

    /// <summary>
    /// The interface bitmaps (8-bit with a palette, or 16-bit 555) as RGBA, bottom row first as Unity
    /// textures want; the magenta the art uses for "nothing here" becomes transparent.
    /// </summary>
    public static class ClassicBitmap
    {
        public static (int Width, int Height, byte[] Rgba) Decode(byte[] bmp)
        {
            if (bmp.Length < 54 || bmp[0] != 'B' || bmp[1] != 'M')
                throw new InvalidDataException("not a BMP");
            int dataOffset = BitConverter.ToInt32(bmp, 10), headerSize = BitConverter.ToInt32(bmp, 14);
            int width = BitConverter.ToInt32(bmp, 18), height = BitConverter.ToInt32(bmp, 22);
            int bpp = BitConverter.ToInt16(bmp, 28);
            bool topDown = height < 0;
            height = Math.Abs(height);
            var rgba = new byte[width * height * 4];
            int stride = bpp == 16 ? (width * 2 + 3) & ~3 : bpp == 8 ? (width + 3) & ~3 : bpp == 24 ? (width * 3 + 3) & ~3
                : throw new InvalidDataException($"{bpp}-bit BMP");
            int paletteAt = 14 + headerSize;
            for (int row = 0; row < height; row++)
            {
                int src = dataOffset + row * stride;
                int y = topDown ? height - 1 - row : row; // BMP rows run bottom-up, like Unity's
                for (int x = 0; x < width; x++)
                {
                    byte r, g, b;
                    if (bpp == 16)
                    {
                        int v = bmp[src + 2 * x] | bmp[src + 2 * x + 1] << 8;
                        r = (byte)(((v >> 10) & 31) * 255 / 31); g = (byte)(((v >> 5) & 31) * 255 / 31); b = (byte)((v & 31) * 255 / 31);
                    }
                    else if (bpp == 8)
                    {
                        int i = paletteAt + 4 * bmp[src + x];
                        b = bmp[i]; g = bmp[i + 1]; r = bmp[i + 2];
                    }
                    else
                    {
                        b = bmp[src + 3 * x]; g = bmp[src + 3 * x + 1]; r = bmp[src + 3 * x + 2];
                    }
                    int o = (y * width + x) * 4;
                    bool magenta = r > 200 && g < 60 && b > 200;
                    if (magenta)
                        r = g = b = 0; // no pink fringe where the scaled art blends into the transparent parts
                    rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = magenta ? (byte)0 : (byte)255;
                }
            }
            return (width, height, rgba);
        }

        /// <summary>
        /// Scale2x (AdvMAME2x): twice the size, the edges of flat areas (letters, button borders) kept
        /// sharp instead of the blur a plain stretch gives; photographic parts are just doubled.
        /// </summary>
        public static (int Width, int Height, byte[] Rgba) Scale2x(int width, int height, byte[] rgba)
        {
            int w2 = width * 2;
            var output = new byte[w2 * height * 2 * 4];
            int Pixel(int x, int y) => BitConverter.ToInt32(rgba, (Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)) * 4);
            void Put(int x, int y, int value)
            {
                int o = (y * w2 + x) * 4;
                output[o] = (byte)value; output[o + 1] = (byte)(value >> 8); output[o + 2] = (byte)(value >> 16); output[o + 3] = (byte)(value >> 24);
            }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int p = Pixel(x, y), a = Pixel(x, y + 1), b = Pixel(x + 1, y), c = Pixel(x - 1, y), d = Pixel(x, y - 1);
                    // a is the row above in the image (rows run bottom-up here), d the row below.
                    Put(2 * x, 2 * y + 1, c == a && c != d && a != b ? a : p);
                    Put(2 * x + 1, 2 * y + 1, a == b && a != c && b != d ? b : p);
                    Put(2 * x, 2 * y, d == c && d != b && c != a ? c : p);
                    Put(2 * x + 1, 2 * y, b == d && b != a && d != c ? d : p);
                }
            return (w2, height * 2, output);
        }
    }
}
