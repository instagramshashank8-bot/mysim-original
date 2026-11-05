using Godot;
using System;
using System.Collections.Generic;
namespace MySim;

// Turns track samples into meshes and collision. Each collision triangle is the same triangle that gets drawn.
public static class TrackBuilder {
    const string Head = "shader_type spatial;\nuniform sampler2D n:filter_linear_mipmap_anisotropic,repeat_enable;\nuniform sampler2D nrm:hint_normal,filter_linear_mipmap_anisotropic,repeat_enable;\nvarying vec3 w;\nvoid vertex(){w=(MODEL_MATRIX*vec4(VERTEX,1.)).xyz;}\n";
    const string RoadShader = Head + @"
void fragment(){
 float f=texture(n,w.xz*1.7).r, g=texture(n,w.xz*.23).r, h=texture(n,w.xz*.021).r;
 vec3 c=vec3(.05)+.055*f+.02*(g-.5)+.022*(h-.5);
 float lat=UV.x, s=UV.y;
 float rub=exp(-pow(lat/3.4,2.))*(.5+.5*g);
 c*=1.-.25*rub;
 float wear=smoothstep(.25,.55,texture(n,w.xz*.31).r);
 float e=smoothstep(.13,.09,abs(abs(lat)-6.0));
 c=mix(c,vec3(.58),e*(.55+.45*wear));
 if(s>0.&&s<3.2){float ch=mod(floor(lat/.8)+floor(s/.8),2.);c=mix(vec3(.03),vec3(.62),ch);}
 ALBEDO=c; ROUGHNESS=.9-.18*rub; SPECULAR=.4;
 NORMAL_MAP=texture(nrm,w.xz*1.7).xyz; NORMAL_MAP_DEPTH=.55;
}";
    const string KerbShader = Head + @"
void fragment(){
 float st=step(mod(UV.y,5.),2.5), f=texture(n,w.xz*1.1).r;
 vec3 c=mix(vec3(.62),vec3(.42,.045,.04),st)*(.8+.35*f);
 ALBEDO=c; ROUGHNESS=.62; SPECULAR=.45;
}";
    const string GrassShader = Head + @"
void fragment(){
 float a=texture(n,w.xz*.013).r, b=texture(n,w.xz*.11).r, c=texture(n,w.xz*.9).r;
 vec3 col=mix(vec3(.082,.13,.048),vec3(.15,.155,.07),smoothstep(.35,.7,a*.7+b*.3));
 col*=.78+.44*c;
 ALBEDO=col; ROUGHNESS=.96; SPECULAR=.22;
 NORMAL_MAP=texture(nrm,w.xz*.6).xyz; NORMAL_MAP_DEPTH=.7;
}";
    const string WallShader = Head + @"
void fragment(){
 float f=texture(n,w.xz*.5+w.y*.7).r, g=texture(n,vec2(UV.y*.07,w.y*.5)).r;
 vec3 c=vec3(.44,.44,.42)*(.72+.45*f);
 c*=mix(.62,1.,smoothstep(.0,.9,w.y-floor(w.y)+g*.4));
 ALBEDO=c; ROUGHNESS=.88; SPECULAR=.35;
}";

    public static Node3D Build(Track t, bool visuals) {
        var root = new Node3D { Name = "Circuit" };
        var ground = new List<Vector3>(); var barrier = new List<Vector3>();
        Material road = null, kerb = null, grass = null, wall = null;
        if (visuals) {
            var noise = NoiseTex(11, 0.012f, 1024, false); var normal = NoiseTex(23, 0.06f, 1024, true);
            road = Mat(RoadShader, noise, normal); kerb = Mat(KerbShader, noise, normal);
            grass = Mat(GrassShader, noise, normal); wall = Mat(WallShader, noise, normal);
        }

        Grid(root, t, 9, (i, j) => t.Point(i, -Track.HalfRoad + j * (2f * Track.HalfRoad / 8f)), j => -Track.HalfRoad + j * (2f * Track.HalfRoad / 8f), _ => true, road, ground, visuals);
        foreach (int side in new[] { -1, 1 }) {
            Func<int, float> lat = j => side < 0 ? -Track.KerbOuter + j * (Track.KerbOuter - Track.HalfRoad) / 5f : Track.HalfRoad + j * (Track.KerbOuter - Track.HalfRoad) / 5f;
            Grid(root, t, 6, (i, j) => t.Point(i, lat(j)), j => lat(j), i => t.Samples[i].Kerb, kerb, ground, visuals);
            Grid(root, t, 6, (i, j) => t.Point(i, lat(j)), j => lat(j), i => !t.Samples[i].Kerb, road, ground, visuals);

            float B(int i) => side < 0 ? t.Samples[i].BarrierLeft : t.Samples[i].BarrierRight;
            Vector3 Out(int i, float a) { // a = distance from the centreline on this side, verge then falling away behind the barrier
                float b = B(i), fall = a > b ? (a - b) * 0.25f : 0f;
                return t.Point(i, side * a) + Vector3.Down * fall;
            }
            Func<int, int, float> A = (i, j) => Track.KerbOuter + j / 6f * (B(i) + 8f - Track.KerbOuter);
            Grid(root, t, 7, (i, j) => side < 0 ? Out(i, A(i, 6 - j)) : Out(i, A(i, j)), j => j, _ => true, grass, ground, visuals);

            // Barrier: three faces, all solid.
            Vector3 Foot(int i, float a) => Out(i, a) + Vector3.Down * 0.3f;
            Vector3 Top(int i, float a) { var p = Out(i, a); p.Y = t.Samples[i].Pos.Y + Track.Profile(B(i), false) + 1.15f; return p; }
            const float thick = 0.35f;
            if (side > 0) {
                Grid(root, t, 2, (i, j) => j == 0 ? Foot(i, B(i)) : Top(i, B(i)), j => j, _ => true, wall, barrier, visuals);
                Grid(root, t, 2, (i, j) => Top(i, B(i) + j * thick), j => j, _ => true, wall, barrier, visuals);
                Grid(root, t, 2, (i, j) => j == 0 ? Top(i, B(i) + thick) : Foot(i, B(i) + thick), j => j, _ => true, wall, barrier, visuals);
            } else {
                Grid(root, t, 2, (i, j) => j == 0 ? Top(i, B(i)) : Foot(i, B(i)), j => j, _ => true, wall, barrier, visuals);
                Grid(root, t, 2, (i, j) => Top(i, B(i) + (1 - j) * thick), j => j, _ => true, wall, barrier, visuals);
                Grid(root, t, 2, (i, j) => j == 0 ? Foot(i, B(i) + thick) : Top(i, B(i) + thick), j => j, _ => true, wall, barrier, visuals);
            }
        }

        BuildTerrain(root, t, grass, ground, visuals, out var hillNoise);
        Solid(root, ground, Car.LayerGround, "GroundBody");
        Solid(root, barrier, Car.LayerBarrier, "BarrierBody");
        if (visuals) { Trees(root, t, hillNoise); Venue(root, t); }
        return root;
    }

    static void Solid(Node3D root, List<Vector3> tris, int layer, string name) {
        var body = new StaticBody3D { Name = name, CollisionLayer = (uint)layer, CollisionMask = 0, PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.3f, Bounce = 0.1f } };
        body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = tris.ToArray(), BackfaceCollision = true } });
        root.AddChild(body);
    }

    // A strip along the whole lap, cols wide. Emits a mesh and appends the same triangles to the collision list.
    static void Grid(Node3D root, Track t, int cols, Func<int, int, Vector3> at, Func<int, float> u, Func<int, bool> active, Material mat, List<Vector3> col, bool visuals) {
        int n = t.Count, rows = n + 1;
        var v = new Vector3[rows * cols]; var nr = new Vector3[rows * cols]; var uv = new Vector2[rows * cols]; var tg = new float[rows * cols * 4];
        for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) v[i * cols + j] = at(i % n, j);
        var idx = new List<int>();
        for (int i = 0; i < n; i++) {
            if (!active(i)) continue;
            for (int j = 0; j < cols - 1; j++) {
                int a = i * cols + j, b = a + 1, c = a + cols, d = c + 1;
                idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
                col.Add(v[a]); col.Add(v[c]); col.Add(v[b]); col.Add(v[b]); col.Add(v[c]); col.Add(v[d]);
            }
        }
        if (!visuals || idx.Count == 0) return;
        for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) {
            Vector3 di = at((i + 1) % n, j) - at((i - 1 + n) % n, j);
            Vector3 dj = at(i % n, Math.Min(j + 1, cols - 1)) - at(i % n, Math.Max(j - 1, 0));
            int k = i * cols + j;
            nr[k] = dj.Cross(di).Normalized();
            var ta = dj.Normalized();
            tg[k * 4] = ta.X; tg[k * 4 + 1] = ta.Y; tg[k * 4 + 2] = ta.Z; tg[k * 4 + 3] = 1f;
            uv[k] = new Vector2(u(j), i < n ? t.Samples[i].S : t.Length);
        }
        AddMesh(root, v, nr, uv, tg, idx.ToArray(), mat, true);
    }

    static void AddMesh(Node3D root, Vector3[] v, Vector3[] nr, Vector2[] uv, float[] tg, int[] idx, Material mat, bool shadows) {
        var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v; arr[(int)Mesh.ArrayType.Normal] = nr; arr[(int)Mesh.ArrayType.TexUV] = uv; arr[(int)Mesh.ArrayType.Tangent] = tg; arr[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        mesh.SurfaceSetMaterial(0, mat);
        root.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
    }

    static void BuildTerrain(Node3D root, Track t, Material grass, List<Vector3> col, bool visuals, out FastNoiseLite noise) {
        noise = new FastNoiseLite { Seed = 5, Frequency = 0.0035f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        const float half = 1100f, cell = 10f; int n = (int)(2 * half / cell) + 1;
        var v = new Vector3[n * n]; var nr = new Vector3[n * n]; var uv = new Vector2[n * n]; var tg = new float[n * n * 4];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) {
            float wx = -half + x * cell, wz = -half + z * cell + 300f;
            v[z * n + x] = new Vector3(wx, t.TerrainHeight(wx, wz, noise, out _), wz);
        }
        var idx = new List<int>((n - 1) * (n - 1) * 6);
        for (int z = 0; z < n - 1; z++) for (int x = 0; x < n - 1; x++) {
            int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
            idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(b); idx.Add(d); idx.Add(c);
            col.Add(v[a]); col.Add(v[b]); col.Add(v[c]); col.Add(v[b]); col.Add(v[d]); col.Add(v[c]);
        }
        if (!visuals) return;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) {
            var dx = v[z * n + Math.Min(x + 1, n - 1)] - v[z * n + Math.Max(x - 1, 0)];
            var dz = v[Math.Min(z + 1, n - 1) * n + x] - v[Math.Max(z - 1, 0) * n + x];
            int k = z * n + x; nr[k] = dz.Cross(dx).Normalized(); uv[k] = new Vector2(x, z);
            tg[k * 4] = 1f; tg[k * 4 + 3] = 1f;
        }
        AddMesh(root, v, nr, uv, tg, idx.ToArray(), grass, false);
    }

    static void Trees(Node3D root, Track t, FastNoiseLite noise) {
        var foliage = new SurfaceTool(); foliage.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 4; i++) {
            float r = 1f - i * 0.2f;
            foliage.AppendFrom(new CylinderMesh { TopRadius = 0f, BottomRadius = r * 2.3f, Height = 3.4f, RadialSegments = 9, Rings = 1 }, 0, new Transform3D(Basis.Identity, new Vector3(0, 2.6f + i * 1.9f, 0)));
        }
        var mesh = foliage.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f, Metallic = 0f, MetallicSpecular = 0.15f });
        var trunk = new SurfaceTool(); trunk.Begin(Mesh.PrimitiveType.Triangles);
        trunk.AppendFrom(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.3f, Height = 2f, RadialSegments = 7, Rings = 1 }, 0, new Transform3D(Basis.Identity, new Vector3(0, 1f, 0)));
        trunk.Commit(mesh);
        mesh.SurfaceSetMaterial(1, new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.065f, 0.045f), Roughness = 1f });

        var rng = new RandomNumberGenerator { Seed = 77 };
        var xf = new List<Transform3D>(); var cs = new List<Color>();
        while (xf.Count < 3800) {
            float x = rng.RandfRange(-1000f, 1000f), z = rng.RandfRange(-750f, 1350f);
            float h = t.TerrainHeight(x, z, noise, out float d);
            if (d < 52f) continue;
            float clump = noise.GetNoise2D(x * 3f, z * 3f);
            if (clump < -0.08f) continue;
            float s = rng.RandfRange(0.9f, 2.1f);
            xf.Add(new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0f, 6.28f)).Scaled(new Vector3(s, s * rng.RandfRange(0.95f, 1.35f), s)), new Vector3(x, h - 0.1f, z)));
            cs.Add(new Color(rng.RandfRange(0.035f, 0.07f), rng.RandfRange(0.075f, 0.12f), rng.RandfRange(0.04f, 0.06f)));
        }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = xf.Count };
        for (int i = 0; i < xf.Count; i++) { mm.SetInstanceTransform(i, xf[i]); mm.SetInstanceColor(i, cs[i]); }
        root.AddChild(new MultiMeshInstance3D { Multimesh = mm });
    }

    // Start/finish gantry and a grandstand on the main straight.
    static void Venue(Node3D root, Track t) {
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.17f, 0.18f), Roughness = 0.45f, Metallic = 0.7f };
        var deck = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.35f, 0.36f), Roughness = 0.85f };
        var seats = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.17f, 0.3f), Roughness = 0.7f };
        var s0 = t.Samples[1];
        var basis = Basis.LookingAt(s0.Tangent, Vector3.Up);
        void Box(Vector3 local, Vector3 size, Material m, Vector3 origin) =>
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = m }, Transform = new Transform3D(basis, origin + basis * local) });
        Box(new Vector3(-9.6f, 3.6f, 0), new Vector3(0.5f, 7.2f, 0.5f), steel, s0.Pos);
        Box(new Vector3(9.6f, 3.6f, 0), new Vector3(0.5f, 7.2f, 0.5f), steel, s0.Pos);
        Box(new Vector3(0, 7.2f, 0), new Vector3(19.6f, 1.5f, 0.5f), steel, s0.Pos);
        root.AddChild(new Label3D { Text = "MYSIM   KESTREL RIDGE", FontSize = 96, PixelSize = 0.012f, Modulate = new Color(0.92f, 0.93f, 0.9f), OutlineSize = 0, Shaded = false, Transform = new Transform3D(basis, s0.Pos + basis * new Vector3(0, 7.2f, 0.27f)) });

        var s1 = t.Samples[60 % t.Count];
        var b1 = Basis.LookingAt(s1.Tangent, Vector3.Up);
        for (int tier = 0; tier < 11; tier++) {
            float lat = -(s1.BarrierLeft + 9f + tier * 1.05f), y = 0.6f + tier * 0.72f;
            var p = s1.Pos + b1 * new Vector3(lat, y, 0);
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.05f, 0.72f + y, 120f), Material = tier % 2 == 0 ? seats : deck }, Transform = new Transform3D(b1, p + Vector3.Down * (y * 0.5f)) });
        }
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(15f, 0.3f, 124f), Material = steel }, Transform = new Transform3D(b1, s1.Pos + b1 * new Vector3(-(s1.BarrierLeft + 14f), 13.6f, 0)) });
        foreach (float z in new[] { -58f, 0f, 58f })
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.4f, 14f, 0.4f), Material = steel }, Transform = new Transform3D(b1, s1.Pos + b1 * new Vector3(-(s1.BarrierLeft + 20.5f), 6.6f, z)) });
    }

    static Texture2D NoiseTex(int seed, float freq, int size, bool normal) {
        var n = new FastNoiseLite { Seed = seed, Frequency = freq, FractalOctaves = 6, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        var img = n.GetSeamlessImage(size, size);
        if (normal) img.BumpMapToNormalMap(2.5f);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    static ShaderMaterial Mat(string code, Texture2D n, Texture2D nrm) {
        var m = new ShaderMaterial { Shader = new Shader { Code = code } };
        m.SetShaderParameter("n", n); m.SetShaderParameter("nrm", nrm);
        return m;
    }
}
