using Godot;
namespace MySim;

public partial class Hud : CanvasLayer {
    public Main Game;
    Label speed, gear, lap, best, pause, hint; ColorRect rpmFill; Control dash; Minimap map; double age;
    static readonly Color Ink = new(0.94f, 0.95f, 0.93f), Dim = new(0.67f, 0.7f, 0.7f), Accent = new(0.95f, 0.42f, 0.16f);

    public override void _Ready() {
        ProcessMode = ProcessModeEnum.Always;
        dash = new Control();
        AddChild(dash);
        dash.AddChild(new ColorRect { Color = new Color(0.03f, 0.04f, 0.05f, 0.62f), Size = new Vector2(250, 124) });
        speed = Text(dash, new Vector2(18, 8), 62, Ink);
        dash.AddChild(TextAt("km/h", new Vector2(150, 44), 18, Dim));
        gear = Text(dash, new Vector2(198, 16), 42, Accent);
        dash.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.12f), Position = new Vector2(18, 98), Size = new Vector2(214, 10) });
        rpmFill = new ColorRect { Color = Accent, Position = new Vector2(18, 98), Size = new Vector2(0, 10) };
        dash.AddChild(rpmFill);

        var times = new Control { Position = new Vector2(24, 22) };
        AddChild(times);
        times.AddChild(new ColorRect { Color = new Color(0.03f, 0.04f, 0.05f, 0.62f), Size = new Vector2(250, 92) });
        lap = Text(times, new Vector2(14, 6), 30, Ink);
        best = Text(times, new Vector2(14, 48), 19, Dim);

        map = new Minimap { Game = Game };
        AddChild(map);

        pause = TextAt("PAUSED\nEsc to resume", Vector2.Zero, 40, Ink);
        pause.HorizontalAlignment = HorizontalAlignment.Center; pause.Visible = false;
        AddChild(pause);
        hint = TextAt("W/S or arrows: drive and brake   A/D: steer   Space: handbrake   C: camera   R: back on track   Esc: pause", Vector2.Zero, 18, Ink);
        AddChild(hint);
    }

    static Label TextAt(string s, Vector2 p, int size, Color c) {
        var l = new Label { Text = s, Position = p };
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c);
        return l;
    }
    static Label Text(Control parent, Vector2 p, int size, Color c) { var l = TextAt("", p, size, c); parent.AddChild(l); return l; }

    public void SetPaused(bool on) => pause.Visible = on;

    static string Time(double t) => t < 0 ? "--:--.---" : $"{(int)(t / 60)}:{t % 60:00.000}";

    public override void _Process(double delta) {
        var car = Game.Car; if (car == null) return;
        var size = GetViewport().GetVisibleRect().Size;
        dash.Position = size - new Vector2(274, 148);
        map.Position = new Vector2(size.X - 214, 22);
        pause.Position = size * 0.5f - new Vector2(150, 60);
        speed.Text = Mathf.RoundToInt(Mathf.Abs(car.ForwardSpeed) * 3.6f).ToString();
        gear.Text = car.Gear < 0 ? "R" : car.Gear.ToString();
        float frac = Mathf.Clamp((car.Rpm - 1000f) / 7200f, 0f, 1f);
        rpmFill.Size = new Vector2(214f * frac, 10f);
        rpmFill.Color = car.Rpm > 7400f ? new Color(1f, 0.18f, 0.12f) : Accent;
        lap.Text = Time(Game.LapTime);
        best.Text = $"Lap {Game.LapsDone + 1}   best {Time(Game.BestLap)}";
        age += delta;
        hint.Position = new Vector2(24, size.Y - 44);
        hint.Modulate = new Color(1, 1, 1, Mathf.Clamp(10f - (float)age, 0f, 1f));
    }
}
