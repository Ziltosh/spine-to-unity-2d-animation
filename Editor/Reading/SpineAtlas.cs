using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SpineTo2DAnimation.Editor.Reading
{
    /// <summary>
    /// One packed image of a libGDX/Spine atlas (Spine 3.8 text format).
    /// Coordinates follow the atlas: origin at the top-left of the page, y pointing down.
    /// </summary>
    public sealed class SpineAtlasRegion
    {
        public string Name;
        public SpineAtlasPage Page;
        public int X;
        public int Y;
        /// <summary>Packed size in the original orientation (before the optional 90° packing rotation).</summary>
        public int Width;
        public int Height;
        public int OriginalWidth;
        public int OriginalHeight;
        /// <summary>Offset of the packed pixels inside the original image, from its bottom-left corner.</summary>
        public int OffsetX;
        public int OffsetY;
        /// <summary>True when the packer rotated the region by 90 degrees inside the page.</summary>
        public bool Rotate;
        public int Index = -1;

        public bool IsWhitespaceStripped => Width != OriginalWidth || Height != OriginalHeight;
    }

    public sealed class SpineAtlasPage
    {
        public string FileName;
        public int Width;
        public int Height;
    }

    public sealed class SpineAtlas
    {
        private readonly Dictionary<string, SpineAtlasRegion> regionsByName = new Dictionary<string, SpineAtlasRegion>(StringComparer.Ordinal);

        public List<SpineAtlasPage> Pages { get; } = new List<SpineAtlasPage>();
        public List<SpineAtlasRegion> Regions { get; } = new List<SpineAtlasRegion>();

        public SpineAtlasRegion FindRegion(string name)
        {
            return name != null && regionsByName.TryGetValue(name, out SpineAtlasRegion region) ? region : null;
        }

        public static SpineAtlas Parse(string atlasText)
        {
            var atlas = new SpineAtlas();
            SpineAtlasPage page = null;
            SpineAtlasRegion region = null;
            bool expectPageName = true;

            using (var reader = new StringReader(atlasText ?? string.Empty))
            {
                string rawLine;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    string line = rawLine.TrimEnd();
                    if (line.Length == 0)
                    {
                        expectPageName = true;
                        region = null;
                        continue;
                    }

                    if (line[0] == '﻿')
                        line = line.Substring(1);

                    bool indented = char.IsWhiteSpace(rawLine[0]);
                    int colon = line.IndexOf(':');

                    if (expectPageName)
                    {
                        page = new SpineAtlasPage { FileName = line.Trim() };
                        atlas.Pages.Add(page);
                        expectPageName = false;
                        region = null;
                        continue;
                    }

                    if (!indented && colon >= 0 && region == null)
                    {
                        ReadPageProperty(page, line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim());
                        continue;
                    }

                    if (!indented)
                    {
                        region = new SpineAtlasRegion { Name = line.Trim(), Page = page };
                        atlas.Regions.Add(region);
                        atlas.regionsByName[region.Name] = region;
                        continue;
                    }

                    if (region != null && colon >= 0)
                        ReadRegionProperty(region, line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim());
                }
            }

            foreach (SpineAtlasRegion parsed in atlas.Regions)
            {
                if (parsed.OriginalWidth == 0 && parsed.OriginalHeight == 0)
                {
                    parsed.OriginalWidth = parsed.Width;
                    parsed.OriginalHeight = parsed.Height;
                }
            }

            return atlas;
        }

        private static void ReadPageProperty(SpineAtlasPage page, string key, string value)
        {
            if (page == null || key != "size")
                return;

            int[] size = ReadInts(value, 2);
            page.Width = size[0];
            page.Height = size[1];
        }

        private static void ReadRegionProperty(SpineAtlasRegion region, string key, string value)
        {
            switch (key)
            {
                case "rotate":
                    // Spine 3.8 writes true/false; later exporters may write an angle.
                    region.Rotate = value == "true" || value == "90";
                    break;
                case "xy":
                    int[] xy = ReadInts(value, 2);
                    region.X = xy[0];
                    region.Y = xy[1];
                    break;
                case "size":
                    int[] size = ReadInts(value, 2);
                    region.Width = size[0];
                    region.Height = size[1];
                    break;
                case "orig":
                    int[] orig = ReadInts(value, 2);
                    region.OriginalWidth = orig[0];
                    region.OriginalHeight = orig[1];
                    break;
                case "offset":
                    int[] offset = ReadInts(value, 2);
                    region.OffsetX = offset[0];
                    region.OffsetY = offset[1];
                    break;
                case "index":
                    region.Index = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
            }
        }

        private static int[] ReadInts(string value, int count)
        {
            string[] parts = value.Split(',');
            if (parts.Length < count)
                throw new FormatException($"Expected {count} integers in atlas value '{value}'.");

            var result = new int[count];
            for (int index = 0; index < count; index++)
                result[index] = int.Parse(parts[index].Trim(), CultureInfo.InvariantCulture);

            return result;
        }
    }
}
