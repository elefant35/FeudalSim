using System.Runtime.InteropServices;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>
/// 20 §6.6 resource node (8 bytes): its type (a <see cref="NodeDef"/> handle), its position inside its 64 m chunk in
/// 1/1024 m, a size class (trees: sapling, pole, timber, veteran) and a state (0 = as generated; deltas change it).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ResourceNode
{
    public ushort Type, X, Z;
    public byte Size, State;
}

/// <summary>
/// 10 §3.8 / 20 §6.6 node scattering (M2-01b-ii): trees, bushes, rocks and plant patches, regenerated per 64 m chunk from
/// the seed with integer-only hashing over the saved byte grids (biome, lithology), so any chunk regenerates identically
/// on every platform and only deltas need saving. Each 8 m cell expects density × 0.0064 nodes of each type allowed there
/// (fixed point, 16 fractional bits); edge-only types need a neighbouring cell of another biome; some need a rock.
/// </summary>
public static class NodeScatter
{
    public const int ChunkM = 64, CellsPerSide = 8, Units = 1024;
    public const byte Sapling = 0, Pole = 1, Timber = 2, Veteran = 3;

    /// <summary>Per-biome fixed-point expectations, built once from content (the only float → int step).</summary>
    public sealed class Table
    {
        internal readonly int[] PerCell;   // [biome * types + type], expected nodes per 8 m cell × 65536
        internal readonly bool[] EdgeOnly;
        internal readonly byte[] Lithology;   // 255 = any
        internal readonly int[] SizeCum;      // [type * 4 + k], cumulative × 65536
        public readonly int Types;
        public readonly NodeKind[] Kinds;

        public Table(ContentDatabase content)
        {
            Types = content.Nodes.Count;
            PerCell = new int[9 * Types];
            EdgeOnly = new bool[Types];
            Lithology = new byte[Types];
            SizeCum = new int[Types * 4];
            Kinds = new NodeKind[Types];
            for (var t = 0; t < Types; t++)
            {
                var n = content.Nodes[t];
                Kinds[t] = n.Kind;
                EdgeOnly[t] = n.EdgeOnly;
                Lithology[t] = n.Lithology is { } l && Enum.TryParse<Lithology>(l, true, out var lith) ? (byte)lith : (byte)255;
                foreach (var (key, perHa) in n.Density)
                {
                    var b = Array.IndexOf(Biomes.Keys, key);
                    if (b > 0) { PerCell[(b * Types) + t] = (int)Math.Round(perHa * 0.0064 * 65536.0, MidpointRounding.AwayFromZero); }
                }

                var sizes = n.Sizes ?? [1f, 0f, 0f, 0f];
                var total = sizes.Sum();
                var cum = 0.0;
                for (var k = 0; k < 4; k++)
                {
                    cum += sizes[k] / total;
                    SizeCum[(t * 4) + k] = k == 3 ? 65536 : (int)Math.Round(cum * 65536.0, MidpointRounding.AwayFromZero);
                }
            }
        }
    }

    /// <summary>Chunks per side of the region (128 for 8,192 m).</summary>
    public static int ChunksPerSide(WorldGrid g) => (g.Size - 1) * (int)g.CellM / ChunkM;

    /// <summary>Generates one chunk's nodes into <paramref name="into"/> (cleared first), in a fixed order.</summary>
    public static void Chunk(WorldGrid g, ulong seed, Table table, int cx, int cz, List<ResourceNode> into)
    {
        into.Clear();
        var n = g.Size;
        var chunkKey = SplitMix64.Mix(seed, (ulong)RngStream.WorldGen, Salt.WorldGenNodes, (ulong)cx, (ulong)cz);
        for (var lr = 0; lr < CellsPerSide; lr++)
        {
            for (var lc = 0; lc < CellsPerSide; lc++)
            {
                int col = (cx * CellsPerSide) + lc, row = (cz * CellsPerSide) + lr;
                if (col >= n - 1 || row >= n - 1) { continue; }
                var i = (row * n) + col;
                var biome = g.Biome[i];
                if (biome == 0 || g.Land[i] == 0 || g.Water[i] is (byte)WaterClass.Lake or (byte)WaterClass.River) { continue; }
                var edge = IsEdge(g, i, col, row);
                var lith = g.Lithology[i];
                var cellKey = SplitMix64.Mix(chunkKey, (ulong)((lr * CellsPerSide) + lc), 0, 0, 0);
                for (var t = 0; t < table.Types; t++)
                {
                    var lambda = table.PerCell[(biome * table.Types) + t];
                    if (lambda == 0 || (table.EdgeOnly[t] && !edge) || (table.Lithology[t] != 255 && table.Lithology[t] != lith)) { continue; }
                    var count = lambda >> 16;
                    if ((int)(SplitMix64.Mix(cellKey, (ulong)t, 0, 0, 0) & 0xFFFF) < (lambda & 0xFFFF)) { count++; }
                    for (var j = 0; j < count; j++)
                    {
                        var h = SplitMix64.Mix(cellKey, (ulong)t, (ulong)(j + 1), 0, 0);
                        var size = (int)((h >> 32) & 0xFFFF);
                        byte s = 0;
                        while (s < 3 && size >= table.SizeCum[(t * 4) + s]) { s++; }
                        into.Add(new ResourceNode
                        {
                            Type = (ushort)t, Size = s,
                            X = (ushort)((lc * 8 * Units) + (int)(h & 8191)), Z = (ushort)((lr * 8 * Units) + (int)((h >> 13) & 8191)),
                        });
                    }
                }
            }
        }
    }

    private static bool IsEdge(WorldGrid g, int i, int col, int row)
    {
        var n = g.Size;
        var b = g.Biome[i];
        if (col > 0 && g.Biome[i - 1] is var w && w != 0 && w != b) { return true; }
        if (col < n - 1 && g.Biome[i + 1] is var e && e != 0 && e != b) { return true; }
        if (row > 0 && g.Biome[i - n] is var nn && nn != 0 && nn != b) { return true; }
        return row < n - 1 && g.Biome[i + n] is var s && s != 0 && s != b;
    }

    /// <summary>A node's world position (m).</summary>
    public static (float X, float Z) Position(WorldGrid g, int cx, int cz, in ResourceNode node)
        => (g.X(cx * CellsPerSide) + (node.X / (float)Units), g.Z(cz * CellsPerSide) + (node.Z / (float)Units));

    /// <summary>10 §3.11 W14 helper: harvestable trees (pole and up) and forage patches (non-toxic plants) within a radius.</summary>
    public static (int Trees, int Forage) Around(WorldGrid g, ulong seed, Table table, float x, float z, float radiusM, ContentDatabase content)
    {
        var buffer = new List<ResourceNode>();
        var per = ChunksPerSide(g);
        var half = (g.Size - 1) * g.CellM / 2f;
        int c0 = Math.Max(0, (int)((x - radiusM + half) / ChunkM)), c1 = Math.Min(per - 1, (int)((x + radiusM + half) / ChunkM));
        int r0 = Math.Max(0, (int)((z - radiusM + half) / ChunkM)), r1 = Math.Min(per - 1, (int)((z + radiusM + half) / ChunkM));
        var (trees, forage) = (0, 0);
        for (var cz = r0; cz <= r1; cz++)
        {
            for (var cx = c0; cx <= c1; cx++)
            {
                Chunk(g, seed, table, cx, cz, buffer);
                foreach (var node in buffer)
                {
                    var (nx, nz) = Position(g, cx, cz, node);
                    if (((nx - x) * (nx - x)) + ((nz - z) * (nz - z)) > radiusM * radiusM) { continue; }
                    if (table.Kinds[node.Type] == NodeKind.Tree && node.Size >= Pole) { trees++; }
                    if (table.Kinds[node.Type] == NodeKind.Patch && !content.Nodes[node.Type].Toxic) { forage++; }
                }
            }
        }

        return (trees, forage);
    }
}
