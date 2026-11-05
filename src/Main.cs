using Godot;
namespace MySim;

public partial class Main : Node3D {
    public Track Track; public Car Car; public Hud Hud; public Camera3D Cam;
    public bool PlayerDrives = true, CamFollow = true;
    public int LapsDone; public double LapTime, BestLap = -1, LastLap = -1;
    Bot bot; int camMode; float throttle, brake, steer; Vector3 camPos; float camYaw; bool passedHalf; int lastIdx; bool lapRunning;
    bool padFound;

    public void SetBot(Bot b) => bot = b;

    static string Env(string k) => System.Environment.GetEnvironmentVariable(k);

    public override void _Ready() {
        ProcessMode = ProcessModeEnum.Always;
        bool visuals = DisplayServer.GetName() != "headless";
        Track = Track.Build();
        var circuit = TrackBuilder.Build(Track, visuals);
        circuit.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(circuit);
        Car = new Car { Name = "Car", Track = Track, ProcessMode = ProcessModeEnum.Pausable };
        AddChild(Car);
        Car.PlaceAt(Track.Count - 6);
        lastIdx = Car.TrackIndex;

        if (Env("MYSIM_CHECKS") != null) { PlayerDrives = false; AddChild(new Checks { Game = this }); return; }
        BuildWorld();
        Cam = new Camera3D { Fov = 62f, Near = 0.1f, Far = 4000f, Current = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(Cam);
        camPos = Car.GlobalPosition + Car.GlobalBasis.Z * 6f + Vector3.Up * 2f; camYaw = Car.GlobalRotation.Y;
        Hud = new Hud { Game = this };
        AddChild(Hud);
        if (Env("MYSIM_BOT") != null) { bot = new Bot(Track); PlayerDrives = false; }
        if (Env("MYSIM_SHOTS") is string dir) { PlayerDrives = false; AddChild(new Shots { Game = this, Dir = dir }); }
    }

    void BuildWorld() {
        var sky = new ProceduralSkyMaterial {
            SkyTopColor = new Color(0.20f, 0.36f, 0.62f), SkyHorizonColor = new Color(0.68f, 0.75f, 0.82f), SkyCurve = 0.12f,
            GroundBottomColor = new Color(0.14f, 0.16f, 0.12f), GroundHorizonColor = new Color(0.55f, 0.6f, 0.62f), SunAngleMax = 18f, SunCurve = 0.12f,
        };
        AddChild(new WorldEnvironment { Environment = new Environment {
            BackgroundMode = Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky, AmbientLightEnergy = 0.75f, ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Agx, TonemapExposure = 1.15f,
            SsaoEnabled = true, SsaoRadius = 1.3f, SsaoIntensity = 1.7f, SsaoPower = 1.4f,
            GlowEnabled = true, GlowIntensity = 0.25f, GlowBloom = 0.02f, GlowHdrThreshold = 1.2f,
            FogEnabled = true, FogLightColor = new Color(0.66f, 0.74f, 0.84f), FogDensity = 0.00035f, FogAerialPerspective = 0.35f, FogSkyAffect = 0f,
        } });
        AddChild(new DirectionalLight3D {
            RotationDegrees = new Vector3(-46f, 38f, 0f), LightColor = new Color(1f, 0.95f, 0.87f), LightEnergy = 1.55f, ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits, DirectionalShadowMaxDistance = 320f,
            DirectionalShadowSplit1 = 0.035f, DirectionalShadowSplit2 = 0.13f, DirectionalShadowSplit3 = 0.42f,
            ShadowBias = 0.035f, ShadowNormalBias = 0.7f, ShadowBlur = 0.75f,
        });
    }

    public override void _UnhandledInput(InputEvent e) {
        bool paused = GetTree().Paused;
        if (e is InputEventKey { Pressed: true, Echo: false } k) {
            if (k.Keycode == Key.Escape) TogglePause();
            else if (paused) return;
            else if (k.Keycode == Key.C) camMode = (camMode + 1) % 3;
            else if (k.Keycode == Key.R) Respawn();
        }
        if (e is InputEventJoypadButton { Pressed: true } b) {
            if (b.ButtonIndex == JoyButton.Start) TogglePause();
            else if (paused) return;
            else if (b.ButtonIndex == JoyButton.Y) camMode = (camMode + 1) % 3;
            else if (b.ButtonIndex == JoyButton.Back) Respawn();
        }
    }

    void TogglePause() { GetTree().Paused = !GetTree().Paused; Hud?.SetPaused(GetTree().Paused); }
    void Respawn() { Car.PlaceAt(Car.TrackIndex); throttle = brake = steer = 0f; }

    public override void _PhysicsProcess(double delta) {
        if (GetTree().Paused) return;
        float dt = (float)delta;
        if (bot != null) bot.Drive(Car);
        else if (PlayerDrives) ReadPlayer(dt);
        if (Car.GlobalPosition.Y < -80f) Respawn();
        TrackLaps(delta);
    }

    void ReadPlayer(float dt) {
        float kThrottle = Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up) ? 1f : 0f;
        float kBrake = Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down) ? 1f : 0f;
        float kSteer = (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left) ? 1f : 0f);
        float pad = Input.GetJoyAxis(0, JoyAxis.LeftX), padT = Input.GetJoyAxis(0, JoyAxis.TriggerRight), padB = Input.GetJoyAxis(0, JoyAxis.TriggerLeft);
        if (Mathf.Abs(pad) < 0.08f) pad = 0f;
        padFound |= Mathf.Abs(pad) > 0.1f || padT > 0.1f || padB > 0.1f;
        // Keyboard keys are on/off, so they ramp; a stick or trigger is already smooth.
        throttle = Mathf.MoveToward(throttle, kThrottle, dt * 6f);
        brake = Mathf.MoveToward(brake, kBrake, dt * 9f);
        steer = Mathf.MoveToward(steer, kSteer, dt * (kSteer == 0f ? 5f : 2.6f));
        Car.Throttle = Mathf.Max(throttle, padFound ? padT : 0f);
        Car.Brake = Mathf.Max(brake, padFound ? padB : 0f);
        Car.Steer = kSteer != 0f || steer != 0f ? steer : (padFound ? pad * Mathf.Abs(pad) * 0.4f + pad * 0.6f : 0f);
        Car.Handbrake = Input.IsPhysicalKeyPressed(Key.Space) || Input.IsJoyButtonPressed(0, JoyButton.A);
    }

    void TrackLaps(double delta) {
        int i = Car.TrackIndex, n = Track.Count;
        if (lapRunning) LapTime += delta;
        if (i > n * 0.4f && i < n * 0.6f) passedHalf = true;
        if (lastIdx > n * 0.8f && i < n * 0.2f) {
            if (lapRunning && passedHalf) {
                LapsDone++; LastLap = LapTime;
                if (BestLap < 0 || LapTime < BestLap) BestLap = LapTime;
            }
            lapRunning = true; LapTime = 0; passedHalf = false;
        }
        lastIdx = i;
    }

    public override void _Process(double delta) {
        if (Cam == null || !CamFollow) return;
        float dt = (float)delta;
        var xf = Car.GetGlobalTransformInterpolated();
        if (camMode == 2) { // bonnet
            Cam.Fov = 68f;
            Cam.GlobalTransform = xf * new Transform3D(Basis.Identity, new Vector3(0f, 0.98f, -0.3f));
            return;
        }
        float back = camMode == 0 ? 5.9f : 9f, high = camMode == 0 ? 1.95f : 3f;
        float yaw = xf.Basis.GetEuler().Y;
        camYaw = Mathf.LerpAngle(camYaw, yaw, 1f - Mathf.Exp(-dt * 7f));
        var behind = new Vector3(Mathf.Sin(camYaw), 0f, Mathf.Cos(camYaw));
        var want = xf.Origin + behind * back + Vector3.Up * high;
        camPos = camPos.Lerp(want, 1f - Mathf.Exp(-dt * 14f));
        Cam.Fov = Mathf.Lerp(Cam.Fov, 60f + Mathf.Min(Car.Speed, 85f) * 0.17f, 1f - Mathf.Exp(-dt * 3f));
        Cam.LookAtFromPosition(camPos, xf.Origin + Vector3.Up * 0.85f - behind * 2.2f, Vector3.Up);
    }
}
