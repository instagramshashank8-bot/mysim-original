using Godot;
using System.Collections.Generic;
namespace MySim;

// One material per material name on the car model, so paint, glass and rubber all answer the same scene light.
public static class CarLook {
    static readonly Dictionary<string, StandardMaterial3D> Table = new() {
        ["paint"] = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.09f, 0.05f), Metallic = 0.35f, Roughness = 0.36f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.06f },
        ["stripe"] = new StandardMaterial3D { AlbedoColor = new Color(0.68f, 0.68f, 0.66f), Metallic = 0.1f, Roughness = 0.4f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.06f },
        ["black"] = new StandardMaterial3D { AlbedoColor = new Color(0.018f, 0.018f, 0.02f), Roughness = 0.62f },
        ["carbon"] = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.03f, 0.033f), Metallic = 0.3f, Roughness = 0.34f, ClearcoatEnabled = true, Clearcoat = 0.6f, ClearcoatRoughness = 0.15f },
        ["glass"] = new StandardMaterial3D { AlbedoColor = new Color(0.015f, 0.02f, 0.025f), Metallic = 0.6f, Roughness = 0.06f },
        ["headlight"] = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.82f, 0.85f), Metallic = 0.5f, Roughness = 0.12f, EmissionEnabled = true, Emission = new Color(0.9f, 0.92f, 1f), EmissionEnergyMultiplier = 0.35f },
        ["taillight"] = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.01f, 0.01f), Roughness = 0.2f, EmissionEnabled = true, Emission = new Color(1f, 0.05f, 0.03f), EmissionEnergyMultiplier = 0.7f },
        ["tyre"] = new StandardMaterial3D { AlbedoColor = new Color(0.024f, 0.024f, 0.025f), Roughness = 0.84f, MetallicSpecular = 0.3f },
        ["rim"] = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.165f, 0.17f), Metallic = 0.9f, Roughness = 0.34f },
        ["disc"] = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.31f), Metallic = 0.95f, Roughness = 0.42f },
        ["caliper"] = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.42f, 0.04f), Roughness = 0.45f, Metallic = 0.2f },
    };

    public static void Apply(Node root) {
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false)) {
            var mi = (MeshInstance3D)n;
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) {
                string name = mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                if (Table.TryGetValue(name, out var m)) mi.SetSurfaceOverrideMaterial(i, m);
                else GD.PushWarning("Car material not in table: " + name);
            }
        }
    }
}
