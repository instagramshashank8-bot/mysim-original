using Godot;
using System;
using System.Threading.Tasks;
namespace MySim;

// Runs on GitHub machines. Each check drives the same car and reads the same visible wheels the player sees.
public partial class Checks : Node {
    public Main Game;
    int fails; Car c; Track t;

    public override void _Ready() { c = Game.Car; t = Game.Track; Run(); }

    void Check(string name, bool ok, string detail) { GD.Print((ok ? "PASS  " : "FAIL  ") + name + "  " + detail); if (!ok) fails++; }
    async Task Ticks(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    async Task Seconds(float s) => await Ticks((int)(s * 120f));
    void Pedals(float th = 0, float br = 0, float st = 0) { c.Throttle = th; c.Brake = br; c.Steer = st; c.Handbrake = false; }
    int At(float x, float z) => t.Locate(new Vector3(x, 0, z));

    // Independent of the car's own logic: a fresh ray under each visible wheel, compared with that wheel's lowest point.
    float WheelGap() {
        float worst = 0f; var space = c.GetWorld3D().DirectSpaceState;
        foreach (var w in c.Wheels) {
            if (w.Pivot == null || !w.Contact) continue;
            var p = w.Pivot.GlobalPosition;
            using var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up, p + Vector3.Down * 2f, Car.LayerGround);
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) continue;
            worst = MathF.Max(worst, MathF.Abs(p.Y - Car.WheelRadius - ((Vector3)hit["position"]).Y));
        }
        return worst;
    }

    async void Run() {
        try {
            TyreChecks(); TrackChecks();
            await Rest(); await Launch(); await Braking(); await SteerSense(); await Reverse(); await Stability(); await Crash(); await Grass(); await FlyingLaps();
        } catch (Exception e) { GD.PrintErr(e.ToString()); fails++; }
        GD.Print(fails == 0 ? "ALL CHECKS PASSED" : $"{fails} CHECKS FAILED");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }

    void TyreChecks() {
        float peak = Tyre.Curve(1f);
        Check("tyre peak at slip 1", MathF.Abs(peak - 1f) < 0.01f && Tyre.Curve(3f) < peak, $"peak {peak:0.000} slide {Tyre.Curve(3f):0.000}");
        var (fx, fy) = Tyre.Force(0.1f, 0.14f, 3200f, 1.2f);
        Check("tyre combined stays within grip", MathF.Sqrt(fx * fx + fy * fy) <= 1.2f * 3200f * 1.001f && fx > 0 && fy < 0, $"fx {fx:0} fy {fy:0}");
        Check("tyre no load no force", Tyre.Force(0.1f, 0.1f, 0f, 1.2f) == (0f, 0f), "");
    }

    void TrackChecks() {
        float minR = float.MaxValue, minGap = float.MaxValue; int n = t.Count;
        foreach (var s in t.Samples) minR = MathF.Min(minR, 1f / MathF.Max(MathF.Abs(s.Curvature), 1e-5f));
        for (int i = 0; i < n; i += 3) for (int j = i + 120; j < n && j < i + n - 120; j += 3) {
            minGap = MathF.Min(minGap, t.Samples[i].Pos.DistanceTo(t.Samples[j].Pos));
        }
        Check("circuit length", t.Length > 2800f && t.Length < 5000f, $"{t.Length:0} m");
        Check("tightest bend", minR > 22f, $"radius {minR:0.0} m");
        Check("track sections do not overlap", minGap > 2f * Track.RunOff + 16f, $"closest {minGap:0.0} m");
        Check("closed loop", t.Samples[0].Pos.DistanceTo(t.Samples[n - 1].Pos) < Track.Step * 1.5f, "");
        var noise = new FastNoiseLite { Seed = 5, Frequency = 0.0035f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        int poke = 0;
        for (int i = 0; i < n; i += 5) foreach (float side in new[] { -1f, 1f }) {
            var s = t.Samples[i]; float b = side < 0 ? s.BarrierLeft : s.BarrierRight;
            for (float a = Track.KerbOuter; a <= b; a += 3f) {
                var p = t.Point(i, side * a);
                if (t.TerrainHeight(p.X, p.Z, noise, out _) > p.Y) poke++;
            }
        }
        Check("terrain stays below verge", poke == 0, $"{poke} points above");
    }

    async Task Rest() {
        c.PlaceAt(10); Pedals(); await Seconds(4f);
        var p = t.Point(10, 0f);
        float ride = c.GlobalPosition.Y - p.Y;
        Check("settles and sits still", c.Speed < 0.1f && c.WheelsOnGround == 4, $"speed {c.Speed:0.000} wheels {c.WheelsOnGround}");
        Check("ride height", MathF.Abs(ride) < 0.05f, $"{ride:0.000} m");
        Check("visible wheels touch the road at rest", c.Wheels[0].Pivot != null && WheelGap() < 0.015f, $"gap {WheelGap() * 1000f:0.0} mm");
        ModelCheck();
    }

    void ModelCheck() {
        float lowBody = float.MaxValue, lowWheel = float.MaxValue;
        foreach (var n in c.FindChildren("*", "MeshInstance3D", true, false)) {
            var mi = (MeshInstance3D)n; var box = mi.GetAabb();
            if (box.Size == Vector3.Zero) continue;
            float low = float.MaxValue;
            for (int i = 0; i < 8; i++) low = MathF.Min(low, c.ToLocal(mi.GlobalTransform * box.GetEndpoint(i)).Y);
            if (mi.Name.ToString().StartsWith("spin")) lowWheel = MathF.Min(lowWheel, low); else if (!mi.Name.ToString().StartsWith("caliper")) lowBody = MathF.Min(lowBody, low);
        }
        Check("model body clears the road", lowBody > 0.04f && lowBody < 0.25f, $"lowest body point {lowBody:0.000} m");
        Check("model tyres reach the road", MathF.Abs(lowWheel) < 0.04f, $"lowest tyre point {lowWheel:0.000} m");
    }

    async Task Launch() {
        c.PlaceAt(At(-260, 0)); Pedals(); await Seconds(1f);
        Pedals(th: 1f);
        float time = 0f, start = c.GlobalPosition.X; float t100 = -1f, t200 = -1f; int idx0 = c.TrackIndex;
        while (time < 25f && (t200 < 0)) { await Ticks(1); time += 1f / 120f; if (t100 < 0 && c.ForwardSpeed > 27.78f) t100 = time; if (c.ForwardSpeed > 55.56f) t200 = time; }
        float lat = MathF.Abs(t.Lateral(c.GlobalPosition, c.TrackIndex));
        Check("0-100 km/h", t100 > 2.8f && t100 < 6.5f, $"{t100:0.00} s");
        Check("0-200 km/h", t200 > 6f && t200 < 18f, $"{t200:0.00} s");
        Check("drives straight with no steering", lat < 1.2f, $"drift {lat:0.00} m");
    }

    async Task Braking() {
        c.PlaceAt(At(-260, 0)); Pedals(); await Seconds(0.5f); Pedals(th: 1f);
        while (c.ForwardSpeed < 27.78f) await Ticks(1);
        var p0 = c.GlobalPosition; Pedals(br: 1f);
        int guard = 0; while (c.ForwardSpeed > 0.3f && guard++ < 1500) await Ticks(1);
        float d = c.GlobalPosition.DistanceTo(p0), lat = MathF.Abs(t.Lateral(c.GlobalPosition, c.TrackIndex));
        Check("100-0 km/h braking", d > 24f && d < 48f, $"{d:0.0} m");
        Check("brakes straight", lat < 1.5f, $"drift {lat:0.00} m");
    }

    async Task SteerSense() {
        c.PlaceAt(At(-260, 0)); Pedals(); await Seconds(0.5f); Pedals(th: 1f);
        while (c.ForwardSpeed < 20f) await Ticks(1);
        var right = c.GlobalBasis.X; var p0 = c.GlobalPosition;
        Pedals(th: 0.3f, st: 0.5f); await Seconds(1f);
        float moved = (c.GlobalPosition - p0).Dot(right);
        Check("steer right goes right", moved > 0.5f && c.AngularVelocity.Y < 0f, $"moved {moved:0.0} m, yaw rate {c.AngularVelocity.Y:0.00}");
    }

    async Task Reverse() {
        c.PlaceAt(At(-100, 0)); Pedals(); await Seconds(1f);
        Pedals(br: 1f); await Seconds(3f);
        Check("holding brake at a stop reverses", c.Gear == -1 && c.ForwardSpeed < -1f, $"gear {c.Gear} speed {c.ForwardSpeed:0.0}");
        Pedals(br: 0f, th: 1f); await Seconds(4f);
        Check("throttle drives forward again", c.Gear >= 1 && c.ForwardSpeed > 3f, $"gear {c.Gear} speed {c.ForwardSpeed:0.0}");
    }

    async Task Stability() {
        c.PlaceAt(At(-300, 2)); Pedals(); await Seconds(0.5f); Pedals(th: 1f);
        while (c.ForwardSpeed < 50f) { await Ticks(1); new Bot(t).Drive(c); c.Throttle = 1f; c.Brake = 0f; }
        Pedals(th: 0.5f, st: 1f); await Seconds(0.45f);
        Pedals(th: 0.3f); await Seconds(2.5f);
        float yaw = MathF.Abs(c.AngularVelocity.Y);
        Check("full lock at 180 km/h stays controllable", yaw < 0.45f && c.WheelsOnGround >= 3 && c.GlobalBasis.Y.Y > 0.9f, $"yaw rate after {yaw:0.00} rad/s");
    }

    async Task Crash() {
        int i = At(80, 0);
        c.PlaceAt(i); Pedals(); await Seconds(0.5f);
        int hits = c.BarrierHits;
        var dir = t.Samples[i].Tangent.Rotated(Vector3.Up, Mathf.DegToRad(-28f));
        c.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), c.GlobalPosition);
        c.LinearVelocity = dir * 45f;
        await Seconds(4f);
        float lat = t.Lateral(c.GlobalPosition, t.Locate(c.GlobalPosition));
        float b = t.Samples[t.Locate(c.GlobalPosition)].BarrierRight;
        Check("barrier stops the car at 160 km/h", c.BarrierHits > hits && lat < b, $"hits {c.BarrierHits - hits}, offset {lat:0.0} of {b:0.0} m");
        Check("car stays upright after the hit", c.GlobalBasis.Y.Y > 0.85f, $"up {c.GlobalBasis.Y.Y:0.00}");
    }

    async Task Grass() {
        int i = At(0, 0);
        c.PlaceAt(i, 14f); Pedals(); await Seconds(1f);
        Pedals(th: 1f); await Seconds(4f);
        float grass = c.ForwardSpeed;
        Check("grass is slower but drivable", grass > 6f && grass < 26f, $"{grass:0.0} m/s after 4 s");
    }

    async Task FlyingLaps() {
        var bot = new Bot(t);
        c.PlaceAt(t.Count - 40); Pedals();
        int hits = c.BarrierHits, laps = 0, last = c.TrackIndex; float lapTime = 0f, best = 0f, sim = 0f;
        float maxLat = 0f, maxGap = 0f, maxRoll = 0f, top = 0f, peakG = 0f; int air = 0, ticks = 0; var vPrev = Vector3.Zero;
        while (laps < 3 && sim < 420f) {
            bot.Drive(c);
            await Ticks(1);
            sim += 1f / 120f; lapTime += 1f / 120f; ticks++;
            int now = c.TrackIndex;
            if (last > t.Count * 0.8f && now < t.Count * 0.2f) { if (laps > 0) best = best == 0 ? lapTime : MathF.Min(best, lapTime); laps++; lapTime = 0f; }
            last = now;
            maxLat = MathF.Max(maxLat, MathF.Abs(t.Lateral(c.GlobalPosition, now)));
            top = MathF.Max(top, c.Speed);
            if (c.WheelsOnGround == 4) maxGap = MathF.Max(maxGap, WheelGap()); else air++;
            maxRoll = MathF.Max(maxRoll, MathF.Abs(Mathf.RadToDeg(MathF.Asin(c.GlobalBasis.X.Y))));
            var a = (c.LinearVelocity - vPrev) * 120f; vPrev = c.LinearVelocity;
            if (ticks > 200 && ticks % 12 == 0) peakG = MathF.Max(peakG, a.Dot(c.GlobalBasis.X) / 9.8f);
        }
        Check("bot finishes 3 laps", laps >= 3, $"{laps} laps in {sim:0} s");
        Check("best lap time plausible", best > 60f && best < 180f, $"{best:0.00} s, average {t.Length / MathF.Max(best, 1f) * 3.6f:0} km/h, top {top * 3.6f:0} km/h");
        Check("never touched a barrier", c.BarrierHits == hits, $"{c.BarrierHits - hits} hits");
        Check("stayed on the track", maxLat < Track.KerbOuter + 1f, $"widest {maxLat:0.0} m");
        Check("no floating wheels at any speed", maxGap < 0.04f, $"worst gap {maxGap * 1000f:0} mm");
        Check("all wheels planted", air < ticks / 200, $"{air} of {ticks} ticks with a wheel up");
        Check("body roll realistic", maxRoll < 6f && maxRoll > 0.5f, $"{maxRoll:0.0} deg");
    }
}
