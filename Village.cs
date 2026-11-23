using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

internal enum Terrain { Grass, Forest, Rock, Water, Bridge }

internal sealed class Goods
{
    public string Label { get; set; } = "";
    public int Priority { get; set; }
}

internal sealed class BuildingData
{
    public string Label { get; set; } = "";
    public string? Near { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public float Seconds { get; set; }
    public float BuildSeconds { get; set; }
    public int Capacity { get; set; }
    public Dictionary<string, int> Cost { get; set; } = new();
}

internal sealed class Rules
{
    public Dictionary<string, int> StartingStock { get; set; } = new();
    public Dictionary<string, Goods> Goods { get; set; } = new();
    public Dictionary<string, int> TaskPriority { get; set; } = new();
    public Dictionary<string, BuildingData> Buildings { get; set; } = new();

    public static Rules Load(string path)
    {
        var rules = JsonSerializer.Deserialize<Rules>(FileAccess.GetFileAsString(path))
            ?? throw new InvalidOperationException($"{path}: lege speldata.");
        foreach (var (good, amount) in rules.StartingStock)
            if (!rules.Goods.ContainsKey(good) || amount < 0)
                throw new InvalidOperationException($"{path}: ongeldige startvoorraad voor {good}.");
        foreach (var (id, building) in rules.Buildings)
        {
            var goods = building.Cost.Keys.Concat(new[] { building.Input, building.Output }.OfType<string>());
            foreach (var good in goods)
                if (!rules.Goods.ContainsKey(good))
                    throw new InvalidOperationException($"{path}: {id} verwijst naar onbekend goed {good}.");
            if (building.Cost.Values.Any(amount => amount <= 0) ||
                (id != "stockyard" && building.BuildSeconds <= 0) ||
                (building.Output != null && (building.Seconds <= 0 || building.Capacity <= 0)) ||
                (building.Near != null && !Enum.TryParse<Terrain>(building.Near, out _)))
                throw new InvalidOperationException($"{path}: ongeldige gebouwdata voor {id}.");
        }
        foreach (string id in new[] { "stockyard", "road", "quarry", "woodcamp", "sawmill" })
            if (!rules.Buildings.ContainsKey(id))
                throw new InvalidOperationException($"{path}: ontbrekend gebouw {id}.");
        foreach (string task in new[] { "construction", "production" })
            if (!rules.TaskPriority.ContainsKey(task))
                throw new InvalidOperationException($"{path}: ontbrekende taakprioriteit {task}.");
        return rules;
    }
}

internal sealed class Building(string kind, Vector2I position, BuildingData data)
{
    public string Kind { get; } = kind;
    public Vector2I Position { get; } = position;
    public BuildingData Data { get; } = data;
    public Dictionary<string, int> Stock { get; } = new();
    public Dictionary<string, int> Delivered { get; } = new();
    public float Progress { get; set; }
    public float BuildProgress { get; set; }
    public bool Supplied => Data.Cost.All(cost => Delivered.GetValueOrDefault(cost.Key) >= cost.Value);
    public bool Complete => BuildProgress >= Data.BuildSeconds;
}

internal sealed record Delivery(Building Source, Building Destination, string Good, string Task);

internal class Walker(Vector2I home)
{
    public Vector2I Tile { get; private set; } = home;
    public Vector2 Visual { get; private set; } = home;
    private readonly Queue<Vector2I> route = new();
    private float progress;

    public void SetRoute(List<Vector2I> path)
    {
        route.Clear();
        foreach (var tile in path.Skip(1)) route.Enqueue(tile);
    }

    public bool Arrived(float seconds)
    {
        if (!route.TryPeek(out var next)) return true;
        progress += seconds * 4.5f;
        Visual = ((Vector2)Tile).Lerp(next, Mathf.Min(progress, 1));
        if (progress < 1) return false;
        progress = 0;
        Tile = route.Dequeue();
        return route.Count == 0;
    }
}

internal sealed class Carrier(Vector2I home) : Walker(home)
{
    public Delivery? Delivery { get; set; }
    public bool Loaded { get; set; }
}

internal sealed class Builder(Vector2I home) : Walker(home)
{
    public Building? Site { get; set; }
    public bool Working { get; set; }
}

internal sealed class Village
{
    public const int Width = 17;
    public const int Height = 11;
    public static readonly Vector2I Depot = new(4, 5);
    private static readonly Vector2I[] Directions = { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up };
    public Rules Rules { get; }
    public HashSet<Vector2I> Roads { get; } = new() { Depot + Vector2I.Right };
    public List<Building> Buildings { get; } = new();
    public Carrier[] Carriers { get; } = { new(Depot + Vector2I.Right), new(Depot + Vector2I.Right) };
    public Builder[] Builders { get; } = { new(Depot + Vector2I.Right) };
    public Building Warehouse { get; }
    public bool Won { get; private set; }

    public Village(Rules rules)
    {
        Rules = rules;
        Warehouse = new Building("stockyard", Depot, rules.Buildings["stockyard"]);
        foreach (var (good, amount) in rules.StartingStock) Warehouse.Stock.Add(good, amount);
        Buildings.Add(Warehouse);
    }

    public static Terrain Ground(Vector2I tile)
    {
        if (tile.X == 8) return tile.Y == 5 ? Terrain.Bridge : Terrain.Water;
        if (tile.X is >= 1 and <= 4 && tile.Y is >= 1 and <= 3) return Terrain.Forest;
        if (tile.X is >= 13 and <= 15 && tile.Y is >= 6 and <= 8) return Terrain.Rock;
        return Terrain.Grass;
    }

    public static bool Inside(Vector2I tile) => tile.X >= 0 && tile.X < Width && tile.Y >= 0 && tile.Y < Height;
    public Building? At(Vector2I tile) => Buildings.FirstOrDefault(building => building.Position == tile);
    private static IEnumerable<Vector2I> Neighbors(Vector2I tile) => Directions.Select(direction => tile + direction);
    private bool Walkable(Vector2I tile) => Roads.Contains(tile) || Ground(tile) == Terrain.Bridge && Inside(tile);
    private IEnumerable<Vector2I> Access(Building building) => Neighbors(building.Position).Where(Walkable);

    public string? Place(string tool, Vector2I tile, bool preview = false)
    {
        if (!Inside(tile)) return "Buiten de kaart.";
        if (tool == "road" && (Walkable(tile) || At(tile)?.Kind == "road")) return null;
        if (Ground(tile) != Terrain.Grass || At(tile) != null || Roads.Contains(tile))
            return "Kies een vrije grastegel.";
        var data = Rules.Buildings[tool];
        if (data.Near is string near && !Neighbors(tile).Any(next => Inside(next) && Ground(next).ToString() == near))
            return near == "Forest" ? "Plaats het kamp naast een bosvak." : "Plaats de groeve naast een rotsvak.";
        if (!preview) Buildings.Add(new Building(tool, tile, data));
        return null;
    }

    private List<Vector2I>? FindRoute(Vector2I start, IEnumerable<Vector2I> goals)
    {
        var targets = goals.ToHashSet();
        if (targets.Count == 0) return null;
        var previous = new Dictionary<Vector2I, Vector2I> { [start] = start };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var tile))
        {
            if (targets.Contains(tile))
            {
                var route = new List<Vector2I> { tile };
                while (tile != start)
                {
                    tile = previous[tile];
                    route.Add(tile);
                }
                route.Reverse();
                return route;
            }
            foreach (var next in Neighbors(tile))
                if (Walkable(next) && previous.TryAdd(next, tile))
                    frontier.Enqueue(next);
        }
        return null;
    }

    public bool Connected(Building building) =>
        FindRoute(Depot + Vector2I.Right, Access(building)) != null;

    private int Demand(Building building, string good) => building.Complete
        ? building.Data.Input == good ? building.Data.Capacity - building.Stock.GetValueOrDefault(good) : 0
        : building.Data.Cost.GetValueOrDefault(good) - building.Delivered.GetValueOrDefault(good);

    private IEnumerable<Delivery> Candidates(Carrier carrier)
    {
        foreach (var source in Buildings)
        {
            if (!source.Complete || source.Stock.Count == 0) continue;
            var pickup = FindRoute(carrier.Tile, Access(source));
            if (pickup == null) continue;
            foreach (var (good, quantity) in source.Stock)
            {
                if (source != Warehouse && source.Data.Output != good) continue;
                int pendingPickup = Carriers.Count(other => !other.Loaded &&
                    other.Delivery is { } trip && trip.Source == source && trip.Good == good);
                if (quantity <= pendingPickup) continue;
                foreach (var destination in Buildings)
                {
                    if (destination == source) continue;
                    int incoming = Carriers.Count(other =>
                        other.Delivery is { } trip && trip.Destination == destination && trip.Good == good);
                    if (Demand(destination, good) > incoming && FindRoute(pickup[^1], Access(destination)) != null)
                        yield return new Delivery(source, destination, good, destination.Complete ? "production" : "construction");
                }
            }
        }
    }

    private void Move(Carrier carrier, float seconds)
    {
        if (carrier.Delivery == null)
        {
            carrier.Delivery = Candidates(carrier)
                .OrderByDescending(delivery => Rules.TaskPriority[delivery.Task])
                .ThenByDescending(delivery => Rules.Goods[delivery.Good].Priority)
                .ThenBy(delivery => delivery.Source == Warehouse)
                .FirstOrDefault();
            if (carrier.Delivery == null) return;
            carrier.SetRoute(FindRoute(carrier.Tile, Access(carrier.Delivery.Source))!);
        }

        if (!carrier.Arrived(seconds)) return;

        var trip = carrier.Delivery;
        if (!carrier.Loaded)
        {
            trip.Source.Stock[trip.Good]--;
            carrier.Loaded = true;
            carrier.SetRoute(FindRoute(carrier.Tile, Access(trip.Destination))!);
            return;
        }

        var inventory = trip.Task == "construction" ? trip.Destination.Delivered : trip.Destination.Stock;
        inventory[trip.Good] = inventory.GetValueOrDefault(trip.Good) + 1;
        carrier.Loaded = false;
        carrier.Delivery = null;
    }

    private void Build(Builder builder, float seconds)
    {
        if (builder.Site == null)
        {
            foreach (var site in Buildings)
            {
                if (site.Complete || !site.Supplied || Builders.Any(other => other.Site == site)) continue;
                var route = FindRoute(builder.Tile, Access(site));
                if (route == null) continue;
                builder.Site = site;
                builder.SetRoute(route);
                break;
            }
        }

        if (builder.Site is not Building target || !builder.Arrived(seconds)) return;
        builder.Working = true;
        target.BuildProgress = Math.Min(target.BuildProgress + seconds, target.Data.BuildSeconds);
        if (!target.Complete) return;
        if (target.Kind == "road") Roads.Add(target.Position);
        builder.Site = null;
        builder.Working = false;
    }

    public void Tick(float seconds)
    {
        foreach (var building in Buildings)
        {
            var data = building.Data;
            if (!building.Complete || data.Output is not string output || !Connected(building)) continue;
            if (building.Stock.GetValueOrDefault(output) >= data.Capacity ||
                (data.Input is string input && building.Stock.GetValueOrDefault(input) == 0)) continue;
            building.Progress += seconds;
            if (building.Progress < data.Seconds) continue;
            building.Progress -= data.Seconds;
            if (data.Input is string ingredient) building.Stock[ingredient]--;
            building.Stock[output] = building.Stock.GetValueOrDefault(output) + 1;
            if (building.Kind == "sawmill") Won = true;
        }
        foreach (var carrier in Carriers) Move(carrier, seconds);
        foreach (var builder in Builders) Build(builder, seconds);
    }
}
