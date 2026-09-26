using System.IO;
using EQClassic.ClientCore;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The Trilogy client's interface art, read from its bmpwad.s3d (copied next to the bundles by the
    /// build, or from the Lantern exports' ui folder in the Editor): the main frame (main1.bmp, the 3D
    /// view in its magenta hole) and the spell gems (spelgems.bmp). Null when the archive is missing:
    /// the interface then draws its own frame.
    /// </summary>
    public sealed class ClassicSkin
    {
        public Texture2D Frame { get; private set; }
        public Texture2D Gems { get; private set; }

        /// <summary>The art's own size: every place in the frame is measured on it.</summary>
        public const float Width = 640f, Height = 480f;

        public static ClassicSkin TryLoad()
        {
            string path = null;
            foreach (var candidate in new[] { Path.Combine(ClientBundles.Directory, "ui", "bmpwad.s3d"), Path.Combine(ClientPaths.Exports, "ui", "bmpwad.s3d") })
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            if (path == null)
                return null;
            try
            {
                var files = PfsArchive.Read(File.ReadAllBytes(path));
                if (!files.TryGetValue("main1.bmp", out var frame) || !files.TryGetValue("spelgems.bmp", out var gems))
                    return null;
                return new ClassicSkin { Frame = Texture(frame), Gems = Texture(gems) };
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"EQClassic: the classic interface art could not be read ({e.Message}); using the plain frame.");
                return null;
            }
        }

        private static Texture2D Texture(byte[] bmp)
        {
            var (w, h, rgba) = ClassicBitmap.Decode(bmp);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.LoadRawTextureData(rgba);
            texture.Apply();
            return texture;
        }

        /// <summary>Texture coordinates of a rectangle given from the top left of a 640 × 480 sheet.</summary>
        public static Rect Uv(float x, float y, float w, float h) => new Rect(x / Width, 1f - (y + h) / Height, w / Width, h / Height);

        /// <summary>
        /// A spell's gem in spelgems.bmp: 22 icons (two blocks of 11 columns, cells of 30 × 23 every 31
        /// pixels, from row 8) in six colours; memicon − 2049 gives the colour (÷ 22) and the icon (mod 22).
        /// </summary>
        public static Rect GemUv(int memIcon)
        {
            int k = memIcon - 2049;
            if (k < 0 || k >= 22 * 6)
                return Uv(1, 0, 30, 23); // the plain blue gem
            int colour = k / 22, icon = k % 22;
            int column = icon % 11, row = 8 + (icon / 11) * 6 + colour;
            return Uv(1 + 31 * column, 23 * row, 30, 23);
        }
    }
}
