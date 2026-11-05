using Godot;
namespace MySim;

public partial class Minimap : Control {
    public Main Game;
    Vector2[] line; Vector2 min; float scale; const float Box = 190f;

    public override void _Ready() {
        var t = Game.Track; line = new Vector2[t.Count / 6 + 2];
        min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
        foreach (var s in t.Samples) { var p = new Vector2(s.Pos.X, s.Pos.Z); min = min.Min(p); max = max.Max(p); }
        scale = (Box - 16f) / Mathf.Max(max.X - min.X, max.Y - min.Y);
        for (int i = 0; i < line.Length; i++) line[i] = Project(t.Samples[i * 6 % t.Count].Pos);
    }
    Vector2 Project(Vector3 p) => (new Vector2(p.X, p.Z) - min) * scale + new Vector2(8, 8);
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw() {
        DrawRect(new Rect2(0, 0, Box, Box), new Color(0.03f, 0.04f, 0.05f, 0.5f));
        DrawPolyline(line, new Color(0.9f, 0.92f, 0.9f, 0.85f), 2.5f, true);
        DrawCircle(Project(Game.Car.GlobalPosition), 5f, new Color(0.95f, 0.42f, 0.16f));
    }
}
