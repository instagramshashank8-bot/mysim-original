using Godot;
using System;
namespace MySim;

public struct TrackSample {
    public Vector3 Pos, Tangent, Right;
    public float S, Curvature, BarrierLeft, BarrierRight;
    public bool Kerb;
}

// The circuit as data. Road, kerbs, verges, barriers and all of their collision are generated from these
// samples, so what you see and what you hit cannot disagree.
public sealed class Track {
    public const float HalfRoad = 6.5f, KerbOuter = 8.1f, RunOff = 22f, Step = 2f;
    public const string Name = "Kestrel Ridge";
    public TrackSample[] Samples;
    public float Length;
    public int Count => Samples.Length;

    // x, z, elevation (metres). A fictional layout, drawn for this game.
    static readonly float[,] Points = {
        {-60,0,0},{100,0,0},{290,0,1},{385,45,2},{405,150,5},{330,225,8},{225,205,10},{150,262,12},
        {172,352,13},{272,392,12},{380,420,9},{432,502,7},{362,585,5},{240,560,4},{120,502,5},{0,525,8},
        {-82,602,11},{-205,622,12},{-302,552,10},{-268,450,7},{-160,402,6},{-118,312,5},{-210,240,3},
        {-340,250,2},{-432,180,1},{-412,70,0},{-320,8,0},{-200,0,0}
    };

    public static Track Build() {
        int n = Points.GetLength(0);
        var dense = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < n; i++) {
            Vector3 p0 = Pt(i - 1, n), p1 = Pt(i, n), p2 = Pt(i + 1, n), p3 = Pt(i + 2, n);
            for (int k = 0; k < 40; k++) {
                float t = k / 40f;
                dense.Add(p1.CubicInterpolate(p2, p0, p3, t));
            }
        }
        float total = 0f;
        for (int i = 0; i < dense.Count; i++) total += dense[i].DistanceTo(dense[(i + 1) % dense.Count]);
        int count = (int)MathF.Round(total / Step);
        var pos = new Vector3[count];
        float want = 0f, walked = 0f; int seg = 0;
        for (int i = 0; i < count; i++) {
            want = i * total / count;
            while (true) {
                float len = dense[seg].DistanceTo(dense[(seg + 1) % dense.Count]);
                if (walked + len >= want || seg == dense.Count - 1) {
                    pos[i] = dense[seg].Lerp(dense[(seg + 1) % dense.Count], Mathf.Clamp((want - walked) / len, 0f, 1f));
                    break;
                }
                walked += len; seg++;
            }
        }
        // Repeated averaging removes curvature spikes: the steering and the road surface both feel them.
        for (int pass = 0; pass < 4; pass++) {
            var next = new Vector3[count];
            for (int i = 0; i < count; i++) {
                Vector3 sum = Vector3.Zero;
                for (int k = -6; k <= 6; k++) sum += pos[((i + k) % count + count) % count];
                next[i] = sum / 13f;
            }
            pos = next;
        }
        var tr = new Track { Samples = new TrackSample[count] };
        float s = 0f;
        for (int i = 0; i < count; i++) {
            var t = (pos[(i + 1) % count] - pos[(i - 1 + count) % count]).Normalized();
            tr.Samples[i].Pos = pos[i];
            tr.Samples[i].Tangent = t;
            tr.Samples[i].Right = t.Cross(Vector3.Up).Normalized();
            tr.Samples[i].S = s;
            s += pos[i].DistanceTo(pos[(i + 1) % count]);
        }
        tr.Length = s;
        for (int i = 0; i < count; i++) {
            var a = tr.Samples[(i - 4 + count) % count]; var b = tr.Samples[(i + 4) % count];
            tr.Samples[i].Curvature = (b.Tangent - a.Tangent).Dot(tr.Samples[i].Right) / (8f * Step);
        }
        // A barrier on the inside of a bend must stay within the bend's radius or the ribbon folds over itself.
        for (int i = 0; i < count; i++) {
            float l = RunOff, r = RunOff; bool kerb = false;
            for (int k = -24; k <= 24; k++) {
                float c = tr.Samples[((i + k) % count + count) % count].Curvature;
                float radius = 1f / MathF.Max(MathF.Abs(c), 1e-4f);
                if (c > 0) r = MathF.Min(r, radius * 0.8f); else l = MathF.Min(l, radius * 0.8f);
                if (MathF.Abs(k) <= 12 && MathF.Abs(c) > 1f / 230f) kerb = true;
            }
            tr.Samples[i].BarrierLeft = MathF.Max(l, KerbOuter + 2f);
            tr.Samples[i].BarrierRight = MathF.Max(r, KerbOuter + 2f);
            tr.Samples[i].Kerb = kerb;
        }
        // Smooth the barrier line so it bends gradually rather than stepping in and out.
        for (int pass = 0; pass < 3; pass++) {
            var l = new float[count]; var r = new float[count];
            for (int i = 0; i < count; i++) {
                for (int k = -10; k <= 10; k++) {
                    ref var q = ref tr.Samples[((i + k) % count + count) % count];
                    l[i] += q.BarrierLeft / 21f; r[i] += q.BarrierRight / 21f;
                }
            }
            for (int i = 0; i < count; i++) { tr.Samples[i].BarrierLeft = l[i]; tr.Samples[i].BarrierRight = r[i]; }
        }
        return tr;
    }

    static Vector3 Pt(int i, int n) {
        i = (i % n + n) % n;
        return new Vector3(Points[i, 0], Points[i, 2], Points[i, 1]);
    }

    // Height of the ground relative to the centreline at a sideways offset.
    public static float Profile(float lat, bool kerb) {
        float a = MathF.Abs(lat);
        if (a <= HalfRoad) { float u = a / HalfRoad; return -0.045f * u * u; }
        if (a <= KerbOuter) {
            float t = (a - HalfRoad) / (KerbOuter - HalfRoad);
            return kerb ? -0.045f + 0.07f * MathF.Sin(MathF.PI * t) : -0.045f - 0.015f * t;
        }
        return -0.06f - 0.01f * (a - KerbOuter);
    }

    public Vector3 Point(int i, float lat) {
        ref var s = ref Samples[(i % Count + Count) % Count];
        return s.Pos + s.Right * lat + Vector3.Up * Profile(lat, s.Kerb);
    }

    // Nearest sample to a world position, searching around a hint; pass hint < 0 for a full search.
    public int Locate(Vector3 p, int hint = -1) {
        int best = 0; float bd = float.MaxValue;
        int from = hint < 0 ? 0 : hint - 40, to = hint < 0 ? Count : hint + 40;
        for (int i = from; i < to; i++) {
            int w = (i % Count + Count) % Count;
            float d = new Vector2(p.X - Samples[w].Pos.X, p.Z - Samples[w].Pos.Z).LengthSquared();
            if (d < bd) { bd = d; best = w; }
        }
        return best;
    }

    public float Lateral(Vector3 p, int i) => (p - Samples[i].Pos).Dot(Samples[i].Right);

    // Ground beyond the circuit: follows the nearest track elevation close in, rolling hills further out.
    public float TerrainHeight(float x, float z, FastNoiseLite noise, out float dist) {
        float bd = float.MaxValue, near = 0f, wsum = 0f, hsum = 0f;
        for (int i = 0; i < Count; i += 12) {
            var p = Samples[i].Pos;
            float dx = x - p.X, dz = z - p.Z, d2 = dx * dx + dz * dz;
            if (d2 < bd) { bd = d2; near = p.Y; }
            float w = 1f / (d2 + 400f); wsum += w; hsum += w * p.Y;
        }
        dist = MathF.Sqrt(bd);
        float t = Mathf.SmoothStep(30f, 170f, dist);
        return Mathf.Lerp(near, hsum / wsum, t) - 0.9f + t * (noise.GetNoise2D(x, z) * 34f + 9f);
    }
}
