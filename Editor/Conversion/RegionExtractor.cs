using System;
using System.Collections.Generic;
using System.IO;
using SpineTo2DAnimation.Editor.Reading;
using UnityEngine;

namespace SpineTo2DAnimation.Editor.Conversion
{
    /// <summary>
    /// Rebuilds each atlas region as a standalone image in its original orientation and size,
    /// restoring whitespace stripping, packing rotation and premultiplied alpha.
    /// </summary>
    public sealed class RegionExtractor : IDisposable
    {
        private readonly string atlasDirectory;
        private readonly Dictionary<SpineAtlasPage, Texture2D> pages = new Dictionary<SpineAtlasPage, Texture2D>();
        private readonly Dictionary<SpineAtlasPage, bool> premultiplied = new Dictionary<SpineAtlasPage, bool>();

        public RegionExtractor(string atlasDirectory)
        {
            this.atlasDirectory = atlasDirectory;
        }

        /// <summary>
        /// Direction of the 90° rotation applied by the packer. The libGDX/Spine packer stores
        /// rotated regions turned counter-clockwise; flip only for atlases produced otherwise.
        /// </summary>
        public bool PackedCounterClockwise { get; set; } = true;

        public bool IsPremultiplied(SpineAtlasPage page)
        {
            LoadPage(page);
            return premultiplied[page];
        }

        /// <summary>Returns the region pixels (bottom-left origin, original size) as straight-alpha RGBA.</summary>
        public Color32[] Extract(SpineAtlasRegion region, out int width, out int height)
        {
            Texture2D page = LoadPage(region.Page);
            bool unpremultiply = premultiplied[region.Page];
            Color32[] source = page.GetPixels32();
            int pageWidth = page.width;
            int pageHeight = page.height;

            width = region.OriginalWidth;
            height = region.OriginalHeight;
            var pixels = new Color32[width * height];

            int packedWidth = region.Width;
            int packedHeight = region.Height;
            int footprintWidth = region.Rotate ? packedHeight : packedWidth;
            int footprintHeight = region.Rotate ? packedWidth : packedHeight;
            int footprintBottom = pageHeight - region.Y - footprintHeight;

            for (int j = 0; j < packedHeight; j++)
            {
                for (int i = 0; i < packedWidth; i++)
                {
                    int fx;
                    int fy;
                    if (!region.Rotate)
                    {
                        fx = i;
                        fy = j;
                    }
                    else if (PackedCounterClockwise)
                    {
                        fx = packedHeight - 1 - j;
                        fy = i;
                    }
                    else
                    {
                        fx = j;
                        fy = packedWidth - 1 - i;
                    }

                    int px = region.X + fx;
                    int py = footprintBottom + fy;
                    if (px < 0 || py < 0 || px >= pageWidth || py >= pageHeight)
                        continue;

                    int dx = region.OffsetX + i;
                    int dy = region.OffsetY + j;
                    if (dx < 0 || dy < 0 || dx >= width || dy >= height)
                        continue;

                    Color32 color = source[py * pageWidth + px];
                    pixels[dy * width + dx] = unpremultiply ? Unpremultiply(color) : color;
                }
            }

            return pixels;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in pages.Values)
                UnityEngine.Object.DestroyImmediate(texture);

            pages.Clear();
        }

        private Texture2D LoadPage(SpineAtlasPage page)
        {
            if (pages.TryGetValue(page, out Texture2D loaded))
                return loaded;

            string path = Path.Combine(atlasDirectory, page.FileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Atlas page image not found: {path}", path);

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
                throw new InvalidDataException($"Atlas page image could not be decoded: {path}");

            pages[page] = texture;
            premultiplied[page] = LooksPremultiplied(texture.GetPixels32());
            return texture;
        }

        /// <summary>
        /// Spine 3.8 atlases do not declare premultiplied alpha. A page is treated as premultiplied
        /// when no color channel ever exceeds its alpha and translucent colored pixels exist.
        /// </summary>
        private static bool LooksPremultiplied(Color32[] pixels)
        {
            bool sawTranslucentColor = false;
            foreach (Color32 pixel in pixels)
            {
                if (pixel.r > pixel.a || pixel.g > pixel.a || pixel.b > pixel.a)
                    return false;

                if (pixel.a > 0 && pixel.a < 255 && (pixel.r | pixel.g | pixel.b) != 0)
                    sawTranslucentColor = true;
            }

            return sawTranslucentColor;
        }

        private static Color32 Unpremultiply(Color32 color)
        {
            if (color.a == 0 || color.a == 255)
                return color;

            float scale = 255f / color.a;
            return new Color32(
                (byte)Mathf.Min(255f, color.r * scale + 0.5f),
                (byte)Mathf.Min(255f, color.g * scale + 0.5f),
                (byte)Mathf.Min(255f, color.b * scale + 0.5f),
                color.a);
        }
    }
}
