using Godot;
using System;
namespace MySim;

// Drives the car through the same four inputs a player has. Used by the checks now, rival cars later.
public sealed class Bot {
    readonly Track t; readonly float[] target;

    public Bot(Track track, float grip = 11f, float decel = 8.5f) {
        t = track; target = new float[t.Count];
        for (int i = 0; i < t.Count; i++) target[i] = MathF.Min(85f, MathF.Sqrt(grip / MathF.Max(MathF.Abs(t.Samples[i].Curvature), 1e-4f)));
        for (int pass = 0; pass < 2; pass++)
            for (int i = t.Count - 1; i >= 0; i--) {
                float next = target[(i + 1) % t.Count];
                target[i] = MathF.Min(target[i], MathF.Sqrt(next * next + 2f * decel * Track.Step));
            }
    }

    public void Drive(Car c) {
        float v = c.Speed;
        int i = c.TrackIndex, n = t.Count;
        float look = Mathf.Clamp(v * 0.55f, 7f, 38f);
        var aim = c.GlobalTransform.AffineInverse() * t.Samples[(i + (int)(look / Track.Step)) % n].Pos;
        float d2 = MathF.Max(aim.X * aim.X + aim.Z * aim.Z, 1f);
        float delta = MathF.Atan(2f * Car.Wheelbase * aim.X / d2);
        c.Steer = Mathf.Clamp(delta / c.MaxSteer, -1f, 1f);
        float err = target[(i + (int)(v * 0.25f / Track.Step)) % n] - v;
        c.Throttle = Mathf.Clamp(err * 0.6f, 0f, 1f);
        c.Brake = Mathf.Clamp(-err * 0.35f, 0f, 1f);
    }
}
