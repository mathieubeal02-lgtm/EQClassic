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
        /// <summary>The spell book (book.bmp): the open book fills the 400 × 320 of the view, from the top left of the sheet.</summary>
        public Texture2D Book { get; private set; }
        /// <summary>The spells' square icons (spelicon.bmp): 40 × 40, five a row; spdat icon − 2500.</summary>
        public Texture2D SpellIcons { get; private set; }
        /// <summary>PERSONA's pieces: main3.bmp (inventory and statistics columns), main4.bmp (worn slot strips, money).</summary>
        public Texture2D Persona { get; private set; }
        public Texture2D Strips { get; private set; }
        /// <summary>The items' icons: dragitem01.bmp to dragitem04.bmp (the last in bmpwad2.s3d).</summary>
        public Texture2D[] ItemIcons { get; private set; }
        /// <summary>main9.bmp (general column, bank vault, merchant, SELL), main6.bmp (trade), main5.bmp (social editor, the corpse's cells).</summary>
        public Texture2D Commerce { get; private set; }
        public Texture2D Trading { get; private set; }
        public Texture2D Social { get; private set; }

        public static Rect SpellIconUv(int icon)
        {
            int k = icon - 2500;
            return k < 0 || k >= 25 ? Uv(0, 0, 40, 40) : Uv(40 * (k % 5), 40 * (k / 5), 40, 40);
        }

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
                // dragitem04 is in bmpwad2.s3d, next to bmpwad.s3d.
                var second = Path.Combine(Path.GetDirectoryName(path) ?? "", "bmpwad2.s3d");
                if (File.Exists(second))
                    foreach (var pair in PfsArchive.Read(File.ReadAllBytes(second)))
                        if (!files.ContainsKey(pair.Key))
                            files[pair.Key] = pair.Value;
                Texture2D Optional(string name) => files.TryGetValue(name, out var bmp) ? Texture(bmp) : null;
                return new ClassicSkin
                {
                    Persona = Optional("main3.bmp"),
                    Commerce = Optional("main9.bmp"),
                    Trading = Optional("main6.bmp"),
                    Social = Optional("main5.bmp"),
                    Strips = Optional("main4.bmp"),
                    ItemIcons = new[] { Optional("dragitem01.bmp"), Optional("dragitem02.bmp"), Optional("dragitem03.bmp"), Optional("dragitem04.bmp") },
                    Frame = Texture(frame), Gems = Texture(gems),
                    Book = files.TryGetValue("book.bmp", out var book) ? Texture(book) : null,
                    SpellIcons = files.TryGetValue("spelicon.bmp", out var icons) ? Texture(icons) : null,
                };
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"EQClassic: the classic interface art could not be read ({e.Message}); using the plain frame.");
                return null;
            }
        }

        private static Texture2D Texture(byte[] bmp)
        {
            var (w0, h0, pixels) = ClassicBitmap.Decode(bmp);
            // The art is 640 × 480: doubled with Scale2x (sharp edges) before the screen's own stretch.
            var (w, h, rgba) = ClassicBitmap.Scale2x(w0, h0, pixels);
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
