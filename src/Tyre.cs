using System;
namespace MySim;

// Combined-slip tyre: one Magic Formula curve applied to the normalised slip vector, so grip is shared
// between braking/driving and cornering and never exceeds the friction limit by more than the curve's peak.
public static class Tyre {
    public const float PeakSlipRatio = 0.10f, PeakSlipAngleTan = 0.14f;
    const float B = 1.732f, C = 1.5f; // peak of sin(C*atan(B*s)) sits exactly at s = 1, falling to about 0.71 of peak in a full slide

    public static float Curve(float s) => MathF.Sin(C * MathF.Atan(B * s));

    // Grip coefficient drops gently as load rises; this is what makes weight transfer change the car's balance.
    public static float Mu(float baseMu, float load, float nominalLoad = 3200f) =>
        baseMu * MathF.Max(0.7f, 1f - 0.06f * (load / nominalLoad - 1f));

    // slipRatio > 0 means the wheel is driving. tanSlipAngle > 0 means the contact patch slides to the car's right.
    // Returns the force along the wheel's forward and right axes.
    public static (float fx, float fy) Force(float slipRatio, float tanSlipAngle, float load, float mu) {
        if (load <= 0f) return (0f, 0f);
        float sx = slipRatio / PeakSlipRatio, sy = tanSlipAngle / PeakSlipAngleTan;
        float s = MathF.Sqrt(sx * sx + sy * sy);
        if (s < 1e-5f) return (0f, 0f);
        float f = Mu(mu, load) * load * Curve(s) / s;
        return (f * sx, -f * sy);
    }
}
