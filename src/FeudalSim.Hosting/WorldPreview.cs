using System.IO.Compression;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Hosting;

/// <summary>A reviewable picture of a generated world (M2-01): hypsometric tints, sea by depth, hill shading; written as PNG.</summary>
public static class WorldPreview
{
    public static byte[] Png(WorldGrid g, int maxSide = 1024)
    {
        var step = Math.Max(1, (int)Math.Ceiling(g.Size / (double)maxSide));
        var w = (g.Size + step - 1) / step;
        var rgb = new byte[w * w * 3];
        for (var r = 0; r < w; r++)
        {
            for (var c = 0; c < w; c++)
            {
                int gr = Math.Min(g.Size - 1, r * step), gc = Math.Min(g.Size - 1, c * step);
                var i = (gr * g.Size) + gc;
                var h = g.Height[i];
                var (cr, cg, cb) = Tint(h, g.Land[i] == 1);
                if (g.Land[i] == 1 && gr > 0 && gc > 0)
                {
                    var shade = Math.Clamp(1 + ((g.Height[i - g.Size - 1] - h) * 0.02f), 0.55f, 1.35f);   // light from the north-west
                    (cr, cg, cb) = (cr * shade, cg * shade, cb * shade);
                }

                var wc = (Sim.WorldGen.WaterClass)g.Water[i];
                if (wc is Sim.WorldGen.WaterClass.River or Sim.WorldGen.WaterClass.Lake) { (cr, cg, cb) = (30, 80, 200); }
                else if (wc == Sim.WorldGen.WaterClass.Stream) { (cr, cg, cb) = (80, 140, 230); }
                else if (wc is Sim.WorldGen.WaterClass.Spring) { (cr, cg, cb) = (255, 60, 60); }
                else if (wc == Sim.WorldGen.WaterClass.MarshPool) { (cr, cg, cb) = (90, 130, 120); }
                var o = ((r * w) + c) * 3;
                (rgb[o], rgb[o + 1], rgb[o + 2]) = ((byte)Math.Clamp(cr, 0, 255), (byte)Math.Clamp(cg, 0, 255), (byte)Math.Clamp(cb, 0, 255));
            }
        }

        return Encode(w, w, rgb);
    }

    /// <summary>Stage 4 provinces (10 §3.4) over a faint relief.</summary>
    public static byte[] LithologyPng(WorldGrid g, int maxSide = 1024)
    {
        var step = Math.Max(1, (int)Math.Ceiling(g.Size / (double)maxSide));
        var w = (g.Size + step - 1) / step;
        var rgb = new byte[w * w * 3];
        (byte, byte, byte)[] colors = [(40, 90, 150), (200, 120, 140), (150, 110, 170), (190, 150, 90), (240, 240, 225), (130, 170, 90), (90, 70, 50)];
        for (var r = 0; r < w; r++)
        {
            for (var c = 0; c < w; c++)
            {
                var i = (Math.Min(g.Size - 1, r * step) * g.Size) + Math.Min(g.Size - 1, c * step);
                var (cr, cg, cb) = colors[Math.Min(g.Lithology[i], (byte)(colors.Length - 1))];
                var o = ((r * w) + c) * 3;
                (rgb[o], rgb[o + 1], rgb[o + 2]) = (cr, cg, cb);
            }
        }

        return Encode(w, w, rgb);
    }

    /// <summary>10 §4 biomes (M2-01b-i): sea, coast, meadow, broadleaf, pine, wetland, river valley, hills & moor, highland; water on top.</summary>
    public static byte[] BiomePng(WorldGrid g, int maxSide = 1024)
    {
        var step = Math.Max(1, (int)Math.Ceiling(g.Size / (double)maxSide));
        var w = (g.Size + step - 1) / step;
        var rgb = new byte[w * w * 3];
        (byte, byte, byte)[] colors = [(40, 90, 150), (235, 220, 160), (190, 210, 110), (60, 125, 55), (40, 85, 60), (110, 140, 120), (150, 185, 80), (150, 120, 120), (215, 215, 215)];
        for (var r = 0; r < w; r++)
        {
            for (var c = 0; c < w; c++)
            {
                var i = (Math.Min(g.Size - 1, r * step) * g.Size) + Math.Min(g.Size - 1, c * step);
                var water = (WaterClass)g.Water[i] is WaterClass.River or WaterClass.Lake or WaterClass.Stream;
                var (cr, cg, cb) = g.Land[i] == 0 ? colors[0] : water ? ((byte)50, (byte)110, (byte)190) : colors[Math.Min(g.Biome[i], (byte)(colors.Length - 1))];
                var o = ((r * w) + c) * 3;
                (rgb[o], rgb[o + 1], rgb[o + 2]) = (cr, cg, cb);
            }
        }

        return Encode(w, w, rgb);
    }

    private static (float R, float G, float B) Tint(float h, bool land)
    {
        if (!land) { var d = Math.Clamp(-h / 40f, 0f, 1f); return (40 - (20 * d), 110 - (50 * d), 170 - (40 * d)); }
        (float H, float R, float G, float B)[] ramp = [(0, 200, 190, 140), (4, 150, 175, 105), (30, 120, 160, 90), (150, 90, 135, 70), (450, 140, 120, 90), (800, 170, 160, 150), (1250, 245, 245, 245)];
        for (var k = 1; k < ramp.Length; k++)
        {
            if (h > ramp[k].H) { continue; }
            var t = (h - ramp[k - 1].H) / (ramp[k].H - ramp[k - 1].H);
            return (ramp[k - 1].R + (t * (ramp[k].R - ramp[k - 1].R)), ramp[k - 1].G + (t * (ramp[k].G - ramp[k - 1].G)), ramp[k - 1].B + (t * (ramp[k].B - ramp[k - 1].B)));
        }

        return (245, 245, 245);
    }

    /// <summary>Minimal PNG: 8-bit RGB, filter 0 per row, one zlib IDAT.</summary>
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++) { raw.WriteByte(0); raw.Write(rgb, y * width * 3, width * 3); }
        using var z = new MemoryStream();
        using (var zl = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) { raw.Position = 0; raw.CopyTo(zl); }
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)width); BE(ihdr, 4, (uint)height);
        (ihdr[8], ihdr[9], ihdr[10], ihdr[11], ihdr[12]) = (8, 2, 0, 0, 0);
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        var crc = new byte[4]; BE(crc, 0, Crc(t, data)); s.Write(crc);
    }

    private static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static uint Crc(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a.Concat(b))
        {
            c ^= x;
            for (var k = 0; k < 8; k++) { c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
        }

        return c ^ 0xFFFFFFFFu;
    }
}
