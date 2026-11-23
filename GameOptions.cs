using System;
using Godot;

internal sealed record GameOptions(
    bool Fullscreen, Vector2I Resolution, bool VSync, int Fps, int GuiPercent, int Master, int Effects)
{
    private const string Path = "user://options.cfg";
    public static readonly Vector2I[] Resolutions =
    {
        new(800, 520), new(960, 620), new(1120, 720), new(1280, 720), new(1280, 800),
        new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160)
    };
    public static readonly int[] FrameLimits = { 30, 60, 120, 144, 0 };
    public static readonly int[] GuiSizes = { 85, 100, 115, 125 };
    public static readonly GameOptions Default = new(false, new Vector2I(1120, 720), true, 60, 100, 80, 80);

    public static GameOptions Load()
    {
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok) return Default;
        var resolution = Read(file, "resolution", Default.Resolution, Variant.Type.Vector2I).AsVector2I();
        int fps = Read(file, "fps", Default.Fps, Variant.Type.Int).AsInt32();
        int gui = Read(file, "gui", Default.GuiPercent, Variant.Type.Int).AsInt32();
        return new GameOptions(
            Read(file, "fullscreen", Default.Fullscreen, Variant.Type.Bool).AsBool(),
            Array.Exists(Resolutions, size => size == resolution) ? resolution : Default.Resolution,
            Read(file, "vsync", Default.VSync, Variant.Type.Bool).AsBool(),
            Array.Exists(FrameLimits, limit => limit == fps) ? fps : Default.Fps,
            Array.Exists(GuiSizes, scale => scale == gui) ? gui : Default.GuiPercent,
            Math.Clamp(Read(file, "master", Default.Master, Variant.Type.Int).AsInt32(), 0, 100),
            Math.Clamp(Read(file, "effects", Default.Effects, Variant.Type.Int).AsInt32(), 0, 100));
    }

    private static Variant Read(ConfigFile file, string key, Variant fallback, Variant.Type type)
    {
        var value = file.GetValue("options", key, fallback);
        return value.VariantType == type ? value : fallback;
    }

    public Error Save()
    {
        var file = new ConfigFile();
        file.SetValue("options", "fullscreen", Fullscreen);
        file.SetValue("options", "resolution", Resolution);
        file.SetValue("options", "vsync", VSync);
        file.SetValue("options", "fps", Fps);
        file.SetValue("options", "gui", GuiPercent);
        file.SetValue("options", "master", Master);
        file.SetValue("options", "effects", Effects);
        return file.Save(Path);
    }
}
