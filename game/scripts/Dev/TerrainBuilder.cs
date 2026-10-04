using Godot;

namespace FeudalSim.Game.Dev;

/// <summary>Builds a render mesh and collision for a sim-generated heightfield (M0-12 approach B; shared by dev scenes).</summary>
public static class TerrainBuilder
{
    public static MeshInstance3D BuildMesh(float[] h, int Size, float Spacing)
    {
        var half = (Size - 1) * Spacing / 2f;
        var verts = new Vector3[Size * Size];
        var normals = new Vector3[Size * Size];
        var colors = new Color[Size * Size];
        for (var z = 0; z < Size; z++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (z * Size) + x;
                verts[i] = new Vector3((x * Spacing) - half, h[i], (z * Spacing) - half);
                var hl = h[(z * Size) + Math.Max(0, x - 1)];
                var hr = h[(z * Size) + Math.Min(Size - 1, x + 1)];
                var hd = h[(Math.Max(0, z - 1) * Size) + x];
                var hu = h[(Math.Min(Size - 1, z + 1) * Size) + x];
                var n = new Vector3(hl - hr, 2 * Spacing, hd - hu).Normalized();
                normals[i] = n;
                // Palette swatches (art/palettes): sand low, grass, moss, rock on steep/high ground.
                colors[i] = n.Y < 0.82f || h[i] > 27 ? new Color("7a7a80") : h[i] < 1.5f ? new Color("c8b48a") : h[i] < 14 ? new Color("6f8f3a") : new Color("5a6b2e");
            }
        }

        var indices = new int[(Size - 1) * (Size - 1) * 6];
        var k = 0;
        for (var z = 0; z < Size - 1; z++)
        {
            for (var x = 0; x < Size - 1; x++)
            {
                var a = (z * Size) + x;
                var b = a + 1;
                var c = a + Size;
                var d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f });
        return new MeshInstance3D { Mesh = mesh, Name = "TerrainMesh" };
    }

    public static StaticBody3D BuildCollision(float[] h, int Size, float Spacing)
    {
        var shape = new HeightMapShape3D { MapWidth = Size, MapDepth = Size, MapData = h };
        var body = new StaticBody3D { Name = "TerrainBody" };
        // HeightMapShape3D samples are 1 unit apart and centred; scale X/Z to the vertex spacing.
        body.AddChild(new CollisionShape3D { Shape = shape, Scale = new Vector3(Spacing, 1, Spacing) });
        return body;
    }

    /// <summary>
    /// Heightfield triangles within ±<paramref name="extentM"/> of the origin, built on the CPU as navigation source
    /// geometry (parsing the render mesh would read it back from the GPU, which Godot warns against at runtime).
    /// </summary>
    public static Vector3[] NavigationFaces(float[] h, int Size, float Spacing, float extentM)
    {
        var half = (Size - 1) * Spacing / 2f;
        var lo = Math.Max(0, (int)MathF.Floor((half - extentM) / Spacing));
        var hi = Math.Min(Size - 1, (int)MathF.Ceiling((half + extentM) / Spacing));
        Vector3 V(int x, int z) => new((x * Spacing) - half, h[(z * Size) + x], (z * Spacing) - half);
        var faces = new List<Vector3>((hi - lo) * (hi - lo) * 6);
        for (var z = lo; z < hi; z++)
        {
            for (var x = lo; x < hi; x++)
            {
                faces.Add(V(x, z)); faces.Add(V(x + 1, z)); faces.Add(V(x, z + 1));
                faces.Add(V(x + 1, z)); faces.Add(V(x + 1, z + 1)); faces.Add(V(x, z + 1));
            }
        }

        return [.. faces];
    }

    public static float SampleHeight(float[] h, int Size, float Spacing, float wx, float wz)
    {
        var half = (Size - 1) * Spacing / 2f;
        var x = Math.Clamp((int)MathF.Round((wx + half) / Spacing), 0, Size - 1);
        var z = Math.Clamp((int)MathF.Round((wz + half) / Spacing), 0, Size - 1);
        return h[(z * Size) + x];
    }
}
