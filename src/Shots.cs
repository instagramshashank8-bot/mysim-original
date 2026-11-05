using Godot;
using System.Threading.Tasks;
namespace MySim;

// Renders review pictures on GitHub machines: close-ups for wheel contact, plus in-game views.
public partial class Shots : Node {
    public Main Game; public string Dir;

    public override void _Ready() { Run(); }

    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    async Task Save(string name) {
        await Frames(4);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(Dir + "/" + name + ".png");
        GD.Print("SHOT ", name);
    }

    async Task Still(string name, Vector3 local, Vector3 lookLocal, float fov) {
        var car = Game.Car;
        var cam = Game.Cam; cam.Fov = fov;
        cam.LookAtFromPosition(car.GlobalTransform * local, car.GlobalTransform * lookLocal, Vector3.Up);
        await Save(name);
    }

    async void Run() {
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        Game.CamFollow = false;
        var car = Game.Car; var t = Game.Track;
        await Frames(90);
        await Still("01_front_three_quarter", new Vector3(3.6f, 1.05f, -5.6f), new Vector3(0, 0.55f, 0), 38f);
        await Still("02_rear_three_quarter", new Vector3(-3.8f, 1.2f, 5.6f), new Vector3(0, 0.55f, 0), 38f);
        await Still("03_side_low", new Vector3(6.5f, 0.32f, 0f), new Vector3(0, 0.45f, 0), 34f);
        await Still("04_front_wheel_contact", new Vector3(1.9f, 0.16f, -2.3f), new Vector3(0.83f, 0.2f, -1.3f), 30f);
        await Still("05_rear_wheel_contact", new Vector3(2.1f, 0.16f, 2.6f), new Vector3(0.83f, 0.2f, 1.36f), 30f);
        await Still("06_top", new Vector3(0.01f, 7.5f, 2.5f), new Vector3(0, 0.5f, 0), 40f);

        Game.CamFollow = true;
        await Frames(10);
        await Save("07_chase_on_grid");

        // Moving views: the bot is the driver and the normal game camera follows.
        var bot = new Bot(t);
        Game.SetBot(bot);
        int[] marks = { t.Count / 8, t.Count * 3 / 8, t.Count * 5 / 8, t.Count * 7 / 8 };
        int k = 0;
        foreach (int m in marks) {
            car.PlaceAt(m - 40);
            car.LinearVelocity = t.Samples[(m - 40 + t.Count) % t.Count].Tangent * 25f;
            await Frames(150);
            await Save($"08_chase_{k++}");
        }
        Game.SetBot(null); car.Throttle = car.Brake = car.Steer = 0f;
        Game.CamFollow = false;
        var cam = Game.Cam; cam.Fov = 52f;
        var s = t.Samples[t.Count / 3];
        cam.LookAtFromPosition(s.Pos + s.Right * (s.BarrierRight + 3f) + Vector3.Up * 2.4f, s.Pos + s.Tangent * 60f, Vector3.Up);
        await Save("09_trackside");
        cam.Fov = 60f;
        cam.LookAtFromPosition(new Vector3(0, 420, -350), new Vector3(0, 0, 300), Vector3.Up);
        await Save("10_aerial");
        GetTree().Quit();
    }
}
