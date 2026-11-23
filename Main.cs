using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main : Node2D
{
    private const int Tile = 42;
    private static readonly Vector2 Origin = new(28, 138);
    private static readonly Color Paper = new("#f5e6c8");
    private static readonly Color Quiet = new("#a9b6a2");
    private static readonly Color Gold = new("#f0c474");
    private static readonly Color Ink = new("#20302b");
    private static readonly Color Panel = new("#202c26");
    private Village village = null!;
    private string tool = "inspect";
    private string feedback = "";
    private Building? selected;
    private Vector2I? roadAnchor;
    private float accumulator;
    private int speed = 1;
    private readonly List<Vector2I> roadPreview = new();
    private readonly HashSet<Vector2I> roadPreviewTiles = new();
    private readonly List<Button> HudControls = new();
    private int completedSites;
    private Button pause = null!;
    private Button fast = null!;
    private readonly Dictionary<string, Button> toolButtons = new();

    public override void _Ready()
    {
        Reset();
        string[] tools = { "inspect", "road", "quarry", "woodcamp", "sawmill" };
        string[] labels = { "Inspecteren", "Wegen", "Steengroeve", "Houthakker", "Zagerij" };
        for (int i = 0; i < tools.Length; i++)
        {
            string id = tools[i];
            toolButtons[id] = AddButton($"{i + 1}  {labels[i]}", 28 + i * 145, 84, 137, () => Choose(id));
        }
        pause = AddButton("Pauze", 803, 84, 86, TogglePause);
        fast = AddButton("1×", 897, 84, 65, ToggleFast);
        AddButton("Menu  Esc", 970, 84, 122, () => ShowMenu(Menu.Pause));
        GetTree().AutoAcceptQuit = false;
        Choose("inspect");
        InitMenus();
    }

    private Button AddButton(string label, float x, float y, float width, Action action)
    {
        var button = new Button
        {
            Text = label,
            Position = new Vector2(x, y),
            Size = new Vector2(width, 36),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeColorOverride("font_color", Paper);
        button.AddThemeColorOverride("font_hover_color", Gold);
        button.AddThemeFontSizeOverride("font_size", GuiFont(14));
        button.Pressed += action;
        HudControls.Add(button);
        AddChild(button);
        return button;
    }

    private void Reset()
    {
        village = new Village(Rules.Load("res://data/demo.json"));
        selected = null;
        completedSites = 1;
        accumulator = 0;
        speed = 1;
        Choose("inspect");
        UpdateSpeed();
    }

    private void UpdateSpeed()
    {
        if (pause == null) return;
        pause.Text = speed == 0 ? "Verder" : "Pauze";
        fast.Text = speed == 3 ? "3×" : "1×";
    }

    private void TogglePause()
    {
        speed = speed == 0 ? 1 : 0;
        UpdateSpeed();
    }

    private void ToggleFast()
    {
        speed = speed == 3 ? 1 : 3;
        UpdateSpeed();
    }

    private void Choose(string id)
    {
        tool = id;
        roadAnchor = null;
        ClearRoadPreview();
        feedback = id switch
        {
            "road" => "Klik of sleep over vrije tegels. Verbind de werkplaatsen met de weg bij de schuur.",
            "quarry" => "Klik op een vrije tegel naast een rotsvak.",
            "woodcamp" => "Klik op een vrije tegel naast het bos.",
            "sawmill" => "Plaats een bouwterrein op een vrije grastegel en verbind het met de weg.",
            _ => "Selecteer een gebouw om voorraad, verbinding en voortgang te bekijken."
        };
        foreach (var (name, button) in toolButtons)
            button.Modulate = name == id ? Gold : Colors.White;
    }

    public override void _Process(double delta)
    {
        UpdateMenus(delta);
        if (currentMenu != Menu.None)
        {
            QueueRedraw();
            return;
        }
        accumulator += (float)Math.Min(delta, 0.1) * speed;
        const float tick = 1f / 30;
        while (accumulator >= tick)
        {
            village.Tick(tick);
            accumulator -= tick;
        }
        int nowComplete = village.Buildings.Count(building => building.Complete);
        if (nowComplete > completedSites) PlayCue(completeSound);
        completedSites = nowComplete;
        QueueRedraw();
    }

    private static Vector2I TileAt(Vector2 pointer)
    {
        var local = (pointer - Origin) / Tile;
        return new Vector2I(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y));
    }

    private void PlaceAt(Vector2I tile)
    {
        if (tool == "inspect")
        {
            selected = village.At(tile);
            return;
        }
        string? error = village.Place(tool, tile);
        if (error != null)
        {
            feedback = error;
            return;
        }
        selected = village.At(tile);
        PlayCue(clickSound);
        feedback = selected == null ? "" : $"{selected.Data.Label} geplaatst.";
        tool = "inspect";
        foreach (var (name, button) in toolButtons)
            button.Modulate = name == tool ? Gold : Colors.White;
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        HandleEscape();
        GetViewport().SetInputAsHandled();
    }

    private void ClearRoadPreview()
    {
        roadPreview.Clear();
        roadPreviewTiles.Clear();
    }

    private void ExtendRoadPreview(Vector2I tile)
    {
        if (roadAnchor is not Vector2I anchor) return;
        while (anchor != tile)
        {
            anchor += anchor.X != tile.X
                ? new Vector2I(Math.Sign(tile.X - anchor.X), 0)
                : new Vector2I(0, Math.Sign(tile.Y - anchor.Y));
            if (roadPreviewTiles.Add(anchor)) roadPreview.Add(anchor);
        }
        roadAnchor = anchor;
    }

    private void CommitRoad()
    {
        string? error = roadPreview.Select(tile => village.Place("road", tile, preview: true))
            .FirstOrDefault(message => message != null);
        if (error != null)
            feedback = $"Route geweigerd. {error}";
        else
        {
            foreach (var tile in roadPreview) village.Place("road", tile);
            selected = roadAnchor is Vector2I last ? village.At(last) : selected;
            feedback = "Weg gepland. Bezorgers en bouwers werken tegel voor tegel.";
            PlayCue(clickSound);
        }
        roadAnchor = null;
        ClearRoadPreview();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (currentMenu != Menu.None) return;
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Key1: Choose("inspect"); break;
                case Key.Key2: Choose("road"); break;
                case Key.Key3: Choose("quarry"); break;
                case Key.Key4: Choose("woodcamp"); break;
                case Key.Key5: Choose("sawmill"); break;
                case Key.Space: TogglePause(); break;
            }
        }
        if (input is not InputEventMouse pointer) return;
        var tile = TileAt(GetGlobalTransformWithCanvas().AffineInverse() * pointer.Position);
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed) Choose("inspect");
            if (mouse.ButtonIndex != MouseButton.Left) return;
            if (tool == "road")
            {
                if (mouse.Pressed && Village.Inside(tile))
                {
                    roadAnchor = tile;
                    roadPreview.Add(tile);
                    roadPreviewTiles.Add(tile);
                }
                else if (!mouse.Pressed && roadAnchor != null)
                {
                    if (Village.Inside(tile))
                    {
                        ExtendRoadPreview(tile);
                        CommitRoad();
                    }
                    else
                    {
                        Choose("road");
                        feedback = "Route geannuleerd buiten de kaart.";
                    }
                }
            }
            else if (mouse.Pressed && Village.Inside(tile)) PlaceAt(tile);
        }
        else if (input is InputEventMouseMotion motion && roadAnchor != null)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
            {
                roadAnchor = null;
                ClearRoadPreview();
            }
            else if (Village.Inside(tile)) ExtendRoadPreview(tile);
        }
    }

    private void Text(string text, float x, float y, int size, Color color) =>
        DrawString(ThemeDB.FallbackFont, new Vector2(x, y), text, HorizontalAlignment.Left, -1, GuiFont(size), color);

    private void Box(Vector2 position, Vector2 size, Color color) => DrawRect(new Rect2(position, size), color);
    private static Vector2 PositionOf(Vector2 tile) => Origin + Tile * tile;
    private void Shape(Color color, params Vector2[] points) => DrawColoredPolygon(points, color);

    private void DrawGround(Vector2I tile)
    {
        var pos = PositionOf(tile);
        var ground = Village.Ground(tile);
        int variant = (tile.X * 11 + tile.Y * 7) % 4;
        var grass = new Color(0.31f + variant * 0.012f, 0.42f + variant * 0.014f, 0.27f);
        Box(pos, Vector2.One * Tile, ground == Terrain.Water ? new Color("#3f7780") : grass);
        DrawRect(new Rect2(pos, Vector2.One * Tile), new Color(0, 0, 0, 0.13f), false);
        if (ground == Terrain.Forest)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector2 tree = pos + new Vector2(9 + 11 * i, 17 + (i % 2) * 8);
                Box(tree + new Vector2(-2, 8), new Vector2(4, 12), new Color("#73533b"));
                Shape(new Color(i == 1 ? "#25493a" : "#315a40"), tree + new Vector2(0, -12),
                    tree + new Vector2(9, 11), tree + new Vector2(-9, 11));
            }
        }
        if (ground == Terrain.Rock)
        {
            Shape(new Color("#75847e"), pos + new Vector2(5, 30), pos + new Vector2(10, 15),
                pos + new Vector2(22, 8), pos + new Vector2(33, 15), pos + new Vector2(37, 33));
            Shape(new Color("#a5aaa0"), pos + new Vector2(10, 15), pos + new Vector2(22, 8),
                pos + new Vector2(33, 15), pos + new Vector2(21, 21));
        }
        if (ground == Terrain.Water)
            DrawLine(pos + new Vector2(8, 13 + variant * 4), pos + new Vector2(26, 13 + variant * 4), new Color("#679aa0"), 2);
        if (ground == Terrain.Bridge)
        {
            Box(pos, Vector2.One * Tile, new Color("#6c9696"));
            for (int i = 0; i < 7; i++)
                Box(pos + new Vector2(i * 6, 6), new Vector2(5, 30), new Color("#ae8056"));
        }
        if (village.Roads.Contains(tile))
        {
            var center = pos + Vector2.One * Tile / 2;
            var sand = new Color("#c7a97b");
            Box(center - Vector2.One * 8, Vector2.One * 16, sand);
            foreach (var direction in new[] { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up })
            {
                var next = tile + direction;
                if (village.Roads.Contains(next) || Village.Ground(next) == Terrain.Bridge || village.At(next) != null)
                    DrawLine(center, center + (Vector2)direction * 21, sand, 16);
            }
        }
    }

    private void DrawGood(string good, Vector2 center)
    {
        switch (good)
        {
            case "base:stone":
                Shape(new Color("#bcc5c2"), center + new Vector2(-6, 4), center + new Vector2(-5, -3),
                    center + new Vector2(1, -6), center + new Vector2(6, -2), center + new Vector2(7, 4));
                break;
            case "base:log":
                DrawLine(center + new Vector2(-5, 3), center + new Vector2(5, -3), new Color("#a56c44"), 7);
                DrawCircle(center + new Vector2(-5, 3), 3.4f, new Color("#e4b77f"));
                break;
            case "base:plank":
                Box(center - new Vector2(7, 4), new Vector2(14, 7), new Color("#e2bb76"));
                break;
        }
    }

    private void DrawBuilding(Building building)
    {
        var pos = PositionOf(building.Position);
        if (building.Kind == "road")
        {
            if (!building.Complete)
            {
                DrawRect(new Rect2(pos + Vector2.One * 8, Vector2.One * 26), Gold, false, 2);
                DrawGood("base:stone", pos + Vector2.One * 21);
            }
            if (selected == building)
                DrawRect(new Rect2(pos + Vector2.One * 2, Vector2.One * 38), Gold, false, 2);
            return;
        }
        Box(pos + new Vector2(3, 4), new Vector2(36, 36), new Color("#334037"));
        if (!building.Complete)
        {
            Box(pos + new Vector2(5, 6), new Vector2(32, 30), new Color("#876341"));
            DrawRect(new Rect2(pos + new Vector2(6, 7), new Vector2(30, 28)), Gold, false, 2);
            DrawLine(pos + new Vector2(9, 30), pos + new Vector2(29, 10), Gold, 3);
        }
        else
        {
            Color roof = new(building.Kind switch
            {
                "stockyard" => "#9b5846", "quarry" => "#87938e",
                "woodcamp" => "#a17a4d", _ => "#44828b"
            });
            Box(pos + new Vector2(8, 17), new Vector2(26, 19), new Color("#cda475"));
            Shape(roof, pos + new Vector2(5, 19), pos + new Vector2(21, 6), pos + new Vector2(37, 19),
                pos + new Vector2(33, 23), pos + new Vector2(8, 23));
            Box(pos + new Vector2(17, 26), new Vector2(8, 10), Ink);
            if (building.Data.Output is string good) DrawGood(good, pos + new Vector2(32, 31));
        }
        if (selected == building)
            DrawRect(new Rect2(pos + Vector2.One * 2, Vector2.One * 38), Gold, false, 2);
    }

    private string SiteState(Building building, Builder? builder)
    {
        if (!village.Connected(building)) return "Geen weg";
        if (!building.Supplied) return "Materiaal";
        if (builder == null) return "Wacht op bouwer";
        return builder.Working ? "In aanbouw" : "Bouwer onderweg";
    }

    private void DrawSiteStatus(Building building)
    {
        if (building.Complete) return;
        var state = SiteState(building, village.Builders.FirstOrDefault(worker => worker.Site == building));
        var (icon, color) = state switch
        {
            "Geen weg" => ("!", new Color("#f0927b")),
            "Materiaal" => ("M", Gold),
            "Wacht op bouwer" => ("B", new Color("#b3c3d9")),
            _ => ("+", new Color("#9ed3ec"))
        };
        var center = PositionOf(building.Position) + new Vector2(33, 9);
        DrawCircle(center, 8.5f, Ink);
        Text(icon, center.X - 4, center.Y + 4, 10, color);
    }

    private void DrawWorkerRoute(Walker worker, Color color)
    {
        Vector2 last = PositionOf(worker.Visual) + Vector2.One * Tile / 2;
        foreach (var tile in worker.RemainingRoute)
        {
            Vector2 point = PositionOf(tile) + Vector2.One * Tile / 2;
            DrawDashedLine(last, point, color, 2, 6);
            last = point;
        }
    }

    private void DrawPlanning()
    {
        foreach (var tile in roadPreview)
        {
            bool valid = village.Place("road", tile, preview: true) == null;
            var color = valid ? new Color(0.97f, 0.78f, 0.43f, 0.55f) : new Color(1, 0.38f, 0.28f, 0.7f);
            Box(PositionOf(tile) + new Vector2(9, 9), new Vector2(24, 24), color);
        }
        var hover = TileAt(GetGlobalMousePosition());
        if (!Village.Inside(hover) || tool == "inspect") return;
        bool canPlace = village.Place(tool, hover, preview: true) == null;
        DrawRect(new Rect2(PositionOf(hover) + Vector2.One * 2, Vector2.One * (Tile - 4)),
            canPlace ? Gold : new Color("#ff8871"), false, 2);
        if (!canPlace || tool == "road") return;
        foreach (var adjacent in village.AdjacentTiles(hover))
        {
            bool connected = village.ConnectedRoad(adjacent);
            if (!connected && village.Place("road", adjacent, preview: true) != null) continue;
            var center = PositionOf(adjacent) + Vector2.One * Tile / 2;
            DrawCircle(center, 5, connected ? new Color("#b5e7a6") : Gold, connected);
        }
    }

    private void DrawPanel()
    {
        Box(new Vector2(762, 138), new Vector2(330, 462), Panel);
        Text("DE EERSTE ZAGERIJ", 784, 172, 16, Gold);
        Text("Verbind de groeve, houthakker", 784, 202, 14, Paper);
        Text("en zagerij met het magazijn.", 784, 222, 14, Paper);
        Text("!  Geen weg     M  Materiaal", 784, 247, 13, Quiet);
        Text("B  Wacht op bouwer     +  Aan het werk", 784, 267, 13, Quiet);
        DrawLine(new Vector2(784, 289), new Vector2(1069, 289), new Color("#42554a"));
        var building = selected ?? village.Buildings.LastOrDefault(b => b.Kind == "sawmill") ?? village.Warehouse;
        Text(building.Data.Label, 784, 322, 21, Paper);
        bool connected = village.Connected(building);
        var builder = village.Builders.FirstOrDefault(worker => worker.Site == building);
        string status = building.Complete
            ? connected ? "Aangesloten op het wegennet" : "Geen aansluiting"
            : SiteState(building, builder);
        Text(status, 784, 349, 15, building.Complete && connected ? Quiet : Gold);
        float y = 381;
        foreach (var (good, cost) in building.Data.Cost)
        {
            DrawGood(good, new Vector2(791, y - 5));
            int inTransit = village.Carriers.Count(carrier => carrier.Loaded &&
                carrier.Delivery is { } trip && trip.Destination == building && trip.Good == good);
            Text($"{village.Rules.Goods[good].Label}  {building.Delivered.GetValueOrDefault(good)}/{cost}  •  {inTransit} onderweg",
                808, y, 13, Paper);
            y += 24;
        }
        if (!building.Complete)
        {
            Text($"Bouwvoortgang  {building.BuildProgress / building.Data.BuildSeconds:P0}", 784, y, 15, Paper);
            Box(new Vector2(784, y + 10), new Vector2(284, 5), Ink);
            Box(new Vector2(784, y + 10), new Vector2(284 * building.BuildProgress / building.Data.BuildSeconds, 5), Gold);
        }
        else
        {
            var goods = building == village.Warehouse
                ? building.Stock.Keys
                : new[] { building.Data.Input, building.Data.Output }.OfType<string>();
            foreach (var good in goods)
            {
                DrawGood(good, new Vector2(791, y - 5));
                Text($"{village.Rules.Goods[good].Label} op voorraad  {building.Stock.GetValueOrDefault(good)}",
                    808, y, 15, Paper);
                y += 24;
            }
        }
        int assigned = village.Carriers.Count(carrier => carrier.Delivery?.Destination == building);
        if (assigned != 0 || builder != null)
            Text($"Bezorgers  {assigned}    Bouwer  {(builder == null ? 0 : 1)}", 784, 515, 13, Quiet);
        if (village.Won)
        {
            Box(new Vector2(779, 534), new Vector2(296, 47), new Color("#435c3d"));
            Text("Gelukt! De eerste plank is gemaakt.", 793, 563, 15, Paper);
        }
    }

    public override void _Draw()
    {
        if (village == null) return;
        Text("Settlement Strategy", 28, 52, 34, Paper);
        Text($"Magazijn   Steen {village.Warehouse.Stock.GetValueOrDefault("base:stone")}   •   Boomstammen {village.Warehouse.Stock.GetValueOrDefault("base:log")}", 540, 34, 16, Paper);
        Text($"Vrije bouwers {village.Builders.Count(builder => builder.Site == null)}/{village.Builders.Length}   •   Vrije bezorgers {village.Carriers.Count(carrier => carrier.Delivery == null)}/{village.Carriers.Length}", 540, 56, 14, Quiet);
        for (int y = 0; y < Village.Height; y++)
        for (int x = 0; x < Village.Width; x++)
            DrawGround(new Vector2I(x, y));
        foreach (var building in village.Buildings) DrawBuilding(building);
        if (currentMenu == Menu.None && selected != null)
        {
            foreach (var carrier in village.Carriers)
                if (carrier.Delivery?.Destination == selected) DrawWorkerRoute(carrier, Gold);
            foreach (var builder in village.Builders)
                if (builder.Site == selected) DrawWorkerRoute(builder, new Color("#8fcde9"));
        }
        foreach (var building in village.Buildings) DrawSiteStatus(building);
        for (int i = 0; i < village.Carriers.Length; i++)
        {
            var carrier = village.Carriers[i];
            var center = PositionOf(carrier.Visual) + new Vector2(16 + i * 10, 25 + i * 3);
            DrawCircle(center + new Vector2(0, 3), 7, Ink);
            DrawCircle(center, 5.5f, new Color(i == 0 ? "#f3d7a6" : "#cae0c0"));
            if (carrier.Loaded && carrier.Delivery is { } trip)
            {
                DrawCircle(center - new Vector2(0, 13), 9, Ink);
                DrawGood(trip.Good, center - new Vector2(0, 13));
            }
        }
        foreach (var builder in village.Builders)
        {
            var center = PositionOf(builder.Visual) + new Vector2(20, 13);
            if (builder.Working && builder.Site is { } site)
                center = center.Lerp(PositionOf(site.Position) + new Vector2(21, 21), 0.65f);
            DrawCircle(center, 8, Ink);
            Shape(new Color("#8fcde9"), center + new Vector2(0, -6), center + new Vector2(6, 0),
                center + new Vector2(0, 6), center + new Vector2(-6, 0));
        }
        if (currentMenu == Menu.None) DrawPlanning();
        DrawPanel();
        Text(feedback, 28, 636, 16, Paper);
        string costs = "Blauwe ruit: bouwer   •   Ronde figuren: bezorgers";
        if (tool != "inspect")
        {
            int count = tool == "road" && roadPreview.Count != 0
                ? roadPreview.Count(tile => !village.Walkable(tile) && village.At(tile)?.Kind != "road") : 1;
            costs = string.Join("   •   ", village.Rules.Buildings[tool].Cost.Select(cost =>
                $"{cost.Value * count} × {village.Rules.Goods[cost.Key].Label} (magazijn: {village.Warehouse.Stock.GetValueOrDefault(cost.Key)})"));
        }
        Text(costs, 28, 658, 14, Gold);
        Text("1–5  Bouwgereedschap     Spatie  Pauzeren     Rechtermuisknop  Inspecteren", 28, 682, 14, Quiet);
    }
}
