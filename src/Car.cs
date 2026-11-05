using Godot;
using System;
namespace MySim;

// The one and only vehicle. Everything that drives it (player, bot, checks) sets the same four inputs.
// Each wheel is a ray down from the body. The visible wheel is put exactly where that ray meets the ground,
// so a wheel cannot hover over or sink through the road.
public partial class Car : RigidBody3D {
    public const int LayerGround = 1, LayerBarrier = 2, LayerCar = 4;
    public const float WheelRadius = 0.34f, Wheelbase = 2.66f, TrackWidth = 1.66f;
    const float MassKg = 1280f, FrontZ = -1.30f, RearZ = 1.36f, HalfTrack = TrackWidth / 2f;
    const float MountY = WheelRadius + 0.15f, DroopLimit = 0.11f, BumpLimit = 0.12f;
    const float SpringF = 62000f, SpringR = 58000f, BarF = 26000f, BarR = 17000f;
    const float DampBump = 3000f, DampRebound = 4600f, Gravity = 9.8f;
    const float WheelInertia = 1.2f, RollFloor = 3f; // RollFloor: slip is measured against at least this speed, which keeps a stopped car calm
    const int WheelSubsteps = 16;
    const float FinalDrive = 3.5f, ShiftTime = 0.11f;
    static readonly float[] Gears = { 3.2f, 2.3f, 1.75f, 1.4f, 1.15f, 0.95f };
    const float ReverseGear = 3.0f;

    // Inputs
    public float Throttle, Brake, Steer;
    public bool Handbrake;

    // Read-outs
    public float Rpm { get; private set; } = 1000f;
    public int Gear { get; private set; } = 1; // -1 reverse
    public float Speed => LinearVelocity.Length();
    public float ForwardSpeed => LinearVelocity.Dot(-GlobalBasis.Z);
    public float SteerAngle { get; private set; }
    public int TrackIndex { get; private set; }
    public int WheelsOnGround { get; private set; }
    public int BarrierHits { get; private set; }

    public sealed class Wheel {
        public Vector3 Mount; public bool Front, Left;
        public float Omega, Compression, Load;
        public bool Contact; public Vector3 Hit, Normal;
        public float LocalY; public double SpinAngle;
        public Node3D Pivot, Spin;
    }
    public readonly Wheel[] Wheels = new Wheel[4];
    public Track Track;
    Node3D model;
    float gearTimer, tc = 1f, reverseTimer, steerNow;

    public override void _Ready() {
        Mass = MassKg;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0f, 0.42f, 0.10f);
        Inertia = new Vector3(1900f, 2150f, 460f);
        CanSleep = false;
        ContinuousCd = true;
        CollisionLayer = LayerCar;
        CollisionMask = LayerGround | LayerBarrier;
        LinearDamp = 0f; AngularDamp = 0f;
        GravityScale = 1f;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.25f, Bounce = 0.12f };
        ContactMonitor = true; MaxContactsReported = 4;
        BodyEntered += b => { if (b is StaticBody3D s && (s.CollisionLayer & LayerBarrier) != 0) BarrierHits++; };

        // Rounded body shell: chamfered corners slide along a barrier rather than catching on it.
        float w = 0.93f, l = 2.2f, y0 = 0.14f, y1 = 0.92f;
        AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = new[] {
            new Vector3(-w+.3f,y0,-l), new Vector3(w-.3f,y0,-l), new Vector3(-w+.3f,y0+.3f,-l), new Vector3(w-.3f,y0+.3f,-l),
            new Vector3(-w,y0,-l+.5f), new Vector3(w,y0,-l+.5f), new Vector3(-w,y1-.2f,-l+.8f), new Vector3(w,y1-.2f,-l+.8f),
            new Vector3(-w,y0,l-.3f), new Vector3(w,y0,l-.3f), new Vector3(-w,y1-.1f,l-.3f), new Vector3(w,y1-.1f,l-.3f),
            new Vector3(-w+.3f,y0,l), new Vector3(w-.3f,y0,l), new Vector3(-w+.3f,y1-.2f,l), new Vector3(w-.3f,y1-.2f,l),
        } } });

        int k = 0;
        foreach (bool front in new[] { true, false })
            foreach (bool left in new[] { true, false })
                Wheels[k++] = new Wheel { Front = front, Left = left, Mount = new Vector3(left ? -HalfTrack : HalfTrack, MountY, front ? FrontZ : RearZ) };

        LoadModel();
    }

    void LoadModel() {
        const string path = "res://assets/car/car.glb";
        if (!ResourceLoader.Exists(path)) return;
        model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        AddChild(model);
        foreach (var w in Wheels) {
            string tag = (w.Front ? "F" : "R") + (w.Left ? "L" : "R");
            w.Pivot = model.FindChild("wheel_" + tag, true, false) as Node3D;
            w.Spin = w.Pivot?.FindChild("spin_" + tag, true, false) as Node3D;
        }
        CarLook.Apply(model);
    }

    // The largest steering angle useful at this speed: enough to reach peak grip but no more,
    // so holding full lock on a keyboard at speed corners hard instead of sliding the front tyres.
    public float MaxSteer => Mathf.Clamp(MathF.Atan(Wheelbase * 1.3f * Gravity / MathF.Max(Speed * Speed, 1f)) + 0.10f, 0.035f, 0.55f);

    public void PlaceAt(int sample, float lateral = 0f) {
        var s = Track.Samples[(sample % Track.Count + Track.Count) % Track.Count];
        var forward = s.Tangent;
        var basis = Basis.LookingAt(forward, Vector3.Up);
        GlobalTransform = new Transform3D(basis, Track.Point(sample, lateral) + Vector3.Up * 0.03f);
        LinearVelocity = Vector3.Zero; AngularVelocity = Vector3.Zero;
        foreach (var w in Wheels) w.Omega = 0f;
        Gear = 1; Rpm = 1000f; tc = 1f; steerNow = 0f;
        TrackIndex = Track.Locate(GlobalPosition);
        ResetPhysicsInterpolation();
    }

    public override void _PhysicsProcess(double delta) {
        float dt = (float)delta;
        var space = GetWorld3D().DirectSpaceState;
        var basis = GlobalBasis; var up = basis.Y;
        if (Track != null) TrackIndex = Track.Locate(GlobalPosition, TrackIndex);

        // Steering is rate-limited, as a real rack is.
        steerNow = Mathf.MoveToward(steerNow, Mathf.Clamp(Steer, -1f, 1f), dt * 6f);
        SteerAngle = steerNow * MaxSteer;
        UpdateGearbox(dt);

        // Suspension.
        WheelsOnGround = 0;
        foreach (var w in Wheels) {
            Vector3 mount = GlobalTransform * w.Mount;
            float reach = WheelRadius + (MountY - WheelRadius) + DroopLimit;
            using var q = PhysicsRayQueryParameters3D.Create(mount, mount - up * reach, LayerGround);
            var hit = space.IntersectRay(q);
            w.Contact = hit.Count > 0;
            if (w.Contact) {
                w.Hit = (Vector3)hit["position"]; w.Normal = (Vector3)hit["normal"];
                float d = mount.DistanceTo(w.Hit);
                w.LocalY = MountY - (d - WheelRadius);
                w.Compression = StaticDeflection(w) + (w.LocalY - WheelRadius);
                WheelsOnGround++;
            } else { w.LocalY = MountY - (MountY - WheelRadius) - DroopLimit; w.Compression = 0f; }
        }
        for (int axle = 0; axle < 2; axle++) {
            Wheel l = Wheels[axle * 2], r = Wheels[axle * 2 + 1];
            float bar = (axle == 0 ? BarF : BarR) * (l.Compression - r.Compression);
            Suspend(l, bar, up, dt); Suspend(r, -bar, up, dt);
        }

        // Tyres and drivetrain.
        float engineAtWheels = EngineTorque() * Ratio();
        float ratio = Ratio();
        float inertia = WheelInertia + 0.1f * ratio * ratio;
        Wheel rl = Wheels[2], rr = Wheels[3];
        float lock_ = Mathf.Clamp(14f * (rl.Omega - rr.Omega), -450f, 450f); // limited-slip diff
        float maxSlip = 0f;
        foreach (var w in Wheels) {
            float drive = w.Front ? 0f : engineAtWheels * 0.5f + (w == rl ? -lock_ : lock_);
            float slip = TyreStep(w, drive, w.Front ? WheelInertia : inertia, basis, up, dt);
            if (!w.Front) maxSlip = MathF.Max(maxSlip, Gear >= 0 ? slip : -slip);
        }
        // Traction control backs the engine off when the rear tyres pass peak slip.
        tc = maxSlip > Tyre.PeakSlipRatio * 1.15f && Speed > 1f ? MathF.Max(0.1f, tc - dt * 9f) : MathF.Min(1f, tc + dt * 3f);

        Aero(basis, up);
        UpdateRpm();
        UpdateVisuals(dt);
    }

    static float StaticDeflection(Wheel w) => (w.Front ? 0.048f : 0.057f);

    void Suspend(Wheel w, float bar, Vector3 up, float dt) {
        if (!w.Contact) { w.Load = 0f; return; }
        Vector3 pos = w.Hit - GlobalPosition;
        float rate = -PointVelocity(pos).Dot(up); // positive while compressing
        float spring = (w.Front ? SpringF : SpringR) * w.Compression;
        float damper = rate * (rate > 0 ? DampBump : DampRebound);
        float over = w.LocalY - WheelRadius - BumpLimit;
        float stop = over > 0 ? over * 400000f : 0f;
        w.Load = MathF.Max(0f, spring + damper + bar + stop);
        ApplyForce(up * w.Load, pos);
    }

    Vector3 PointVelocity(Vector3 offset) => LinearVelocity + AngularVelocity.Cross(offset - (GlobalBasis * CenterOfMass));

    // Returns the slip ratio. The spin of a wheel reacts far faster than the car, so it gets its own small steps.
    float TyreStep(Wheel w, float drive, float inertia, Basis basis, Vector3 up, float dt) {
        float brake = (w.Front ? 1950f : 1250f) * BrakePedal();
        bool locked = Handbrake && !w.Front;
        if (locked) brake = 3200f;
        if (!w.Contact) { Integrate(w, drive - w.Omega * 0.5f, brake, inertia, dt); return 0f; }

        Vector3 pos = w.Hit - GlobalPosition;
        Vector3 fwd = -basis.Z;
        if (w.Front) fwd = fwd.Rotated(up, -SteerAngle);
        fwd = (fwd - w.Normal * fwd.Dot(w.Normal)).Normalized();
        Vector3 side = fwd.Cross(w.Normal).Normalized();
        Vector3 vel = PointVelocity(pos);
        float vx = vel.Dot(fwd), vy = vel.Dot(side);

        int i = Track?.Lateral(w.Hit, TrackIndex) is float lat && MathF.Abs(lat) > Track.KerbOuter ? 1 : 0;
        float mu = i == 1 ? 0.62f : 1.28f;

        float tanAlpha = vy / MathF.Max(MathF.Abs(vx), RollFloor);
        float sub = dt / WheelSubsteps, sumX = 0f, sumY = 0f, slip = 0f;
        for (int k = 0; k < WheelSubsteps; k++) {
            slip = (w.Omega * WheelRadius - vx) / MathF.Max(MathF.Abs(vx), RollFloor);
            var (fx, fy) = Tyre.Force(slip, tanAlpha, w.Load, mu);
            sumX += fx; sumY += fy;
            float b = brake;
            if (!locked && slip < -Tyre.PeakSlipRatio * 1.1f && MathF.Abs(vx) > 2f) // ABS
                b *= Mathf.Clamp(1f - (-slip - Tyre.PeakSlipRatio * 1.1f) / (0.5f * Tyre.PeakSlipRatio), 0.15f, 1f);
            Integrate(w, drive - fx * WheelRadius, b, inertia, sub);
        }
        float fX = sumX / WheelSubsteps, fY = sumY / WheelSubsteps;
        float roll = (i == 1 ? 0.09f : 0.014f) * w.Load * MathF.Tanh(vx);
        ApplyForce(fwd * (fX - roll) + side * fY, pos);
        return slip;
    }

    static void Integrate(Wheel w, float torque, float brake, float inertia, float dt) {
        w.Omega += torque / inertia * dt;
        float stop = brake / inertia * dt;
        w.Omega = MathF.Abs(w.Omega) <= stop ? 0f : w.Omega - MathF.Sign(w.Omega) * stop;
    }

    float BrakePedal() => Gear < 0 ? Mathf.Clamp(Throttle, 0f, 1f) : Mathf.Clamp(Brake, 0f, 1f);
    float DrivePedal() => (Gear < 0 ? Mathf.Clamp(Brake, 0f, 1f) : Mathf.Clamp(Throttle, 0f, 1f)) * tc;

    float Ratio() => gearTimer > 0f ? 0f : (Gear < 0 ? -ReverseGear : Gears[Gear - 1]) * FinalDrive;

    float EngineTorque() {
        if (gearTimer > 0f) return 0f;
        float t = TorqueAt(MathF.Max(Rpm, 3200f)) * DrivePedal(); // below 3200 the clutch is slipping
        if (Rpm > 8100f) t = 0f;
        if (DrivePedal() < 0.02f) t = -(35f + 0.010f * Rpm); // engine braking
        return t;
    }

    static float TorqueAt(float rpm) {
        float[,] c = { { 1000, 340 }, { 3000, 520 }, { 5500, 620 }, { 7500, 560 }, { 8200, 430 } };
        for (int i = 0; i < 4; i++)
            if (rpm <= c[i + 1, 0]) return Mathf.Lerp(c[i, 1], c[i + 1, 1], (rpm - c[i, 0]) / (c[i + 1, 0] - c[i, 0]));
        return c[4, 1];
    }

    void UpdateRpm() {
        float r = MathF.Abs(Ratio() != 0f ? Ratio() : (Gear < 0 ? ReverseGear : Gears[Gear - 1]) * FinalDrive);
        float wheel = (MathF.Abs(Wheels[2].Omega) + MathF.Abs(Wheels[3].Omega)) * 0.5f;
        Rpm = MathF.Max(1000f, wheel * r * 60f / MathF.Tau);
    }

    void UpdateGearbox(float dt) {
        if (gearTimer > 0f) { gearTimer -= dt; return; }
        float fwd = ForwardSpeed;
        // Holding brake when stopped selects reverse; throttle selects first.
        if (Gear > 0 && MathF.Abs(fwd) < 1f && Brake > 0.3f && Throttle < 0.05f) {
            reverseTimer += dt;
            if (reverseTimer > 0.25f) { Gear = -1; gearTimer = ShiftTime; reverseTimer = 0f; }
            return;
        }
        reverseTimer = 0f;
        if (Gear < 0) {
            if (Throttle > 0.3f && fwd > -1.5f) { Gear = 1; gearTimer = ShiftTime; }
            return;
        }
        if (Rpm > 7700f && Gear < Gears.Length) { Gear++; gearTimer = ShiftTime; return; }
        if (Gear > 1) {
            float lowerRpm = Rpm * Gears[Gear - 2] / Gears[Gear - 1];
            if (lowerRpm < (Throttle > 0.5f ? 6600f : 5400f)) { Gear--; gearTimer = ShiftTime * 0.6f; }
        }
    }

    void Aero(Basis basis, Vector3 up) {
        float v = LinearVelocity.Length();
        if (v < 0.1f) return;
        float q = 0.5f * 1.2f * v * v;
        ApplyCentralForce(-LinearVelocity.Normalized() * q * 0.72f);
        float down = q * 1.7f * MathF.Max(0f, up.Y);
        ApplyForce(-up * down * 0.46f, basis * new Vector3(0, 0.5f, FrontZ));
        ApplyForce(-up * down * 0.54f, basis * new Vector3(0, 0.5f, RearZ));
    }

    void UpdateVisuals(float dt) {
        foreach (var w in Wheels) {
            w.SpinAngle = Mathf.Wrap(w.SpinAngle - w.Omega * dt, -Math.PI, Math.PI);
            if (w.Pivot == null) continue;
            w.Pivot.Position = new Vector3(w.Mount.X, w.LocalY, w.Mount.Z);
            w.Pivot.Rotation = new Vector3(0f, w.Front ? -SteerAngle : 0f, 0f);
            if (w.Spin != null) w.Spin.Rotation = new Vector3((float)w.SpinAngle, 0f, 0f);
        }
    }
}
