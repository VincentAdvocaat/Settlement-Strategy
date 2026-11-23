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
    public int Yield { get; set; } = 1;
    public int Housing { get; set; }
    public Dictionary<string, int> Cost { get; set; } = new();
}

internal sealed class Rules
{
    public Dictionary<string, int> ResearchCosts { get; set; } = new();
    public Dictionary<string, int> StartingStock { get; set; } = new();
    public Dictionary<string, Goods> Goods { get; set; } = new();
    public Dictionary<string, int> TaskPriority { get; set; } = new();
    public Dictionary<string, BuildingData> Buildings { get; set; } = new();

    public static Rules Load(string path, bool includeMods = true)
    {
        var rules = Read(path);
        const string modFolder = "user://mods";
        foreach (var file in (includeMods && DirAccess.DirExistsAbsolute(modFolder) ? DirAccess.GetFilesAt(modFolder) : Array.Empty<string>()).Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(file => file, StringComparer.Ordinal))
        {
            string modPath = $"{modFolder}/{file}";
            var mod = Read(modPath);
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (name == "base" || name.Contains(':') || mod.StartingStock.Count != 0 || mod.TaskPriority.Count != 0 || mod.ResearchCosts.Count != 0)
                throw new InvalidOperationException($"{modPath}: gebruik een eigen namespace en definieer alleen goederen en gebouwen.");
            var ids = mod.Goods.Keys.Concat(mod.Buildings.Keys);
            foreach (var id in ids)
                if (!id.StartsWith(name + ":", StringComparison.Ordinal) || id.Length <= name.Length + 1)
                    throw new InvalidOperationException($"{modPath}: ID {id} moet beginnen met {name}:.");
            foreach (var (id, good) in mod.Goods)
                if (!rules.Goods.TryAdd(id, good)) throw new InvalidOperationException($"{modPath}: dubbel goed {id}.");
            foreach (var (id, type) in mod.Buildings)
                if (!rules.Buildings.TryAdd(id, type)) throw new InvalidOperationException($"{modPath}: dubbel gebouw {id}.");
            Validate(mod.Buildings, rules.Goods, modPath);
        }
        Validate(rules.Buildings, rules.Goods, path);
        foreach (var (good, amount) in rules.StartingStock)
            if (!rules.Goods.ContainsKey(good) || amount < 0)
                throw new InvalidOperationException($"{path}: ongeldige startvoorraad voor {good}.");
        foreach (string id in new[] { "stockyard", "road", "paved", "quarry", "woodcamp", "sawmill", "farm", "mill", "bakery", "house", "outpost", "research", "expand" })
            if (!rules.Buildings.ContainsKey(id))
                throw new InvalidOperationException($"{path}: ontbrekend gebouw {id}.");
        foreach (string task in new[] { "construction", "production", "village", "outpost" })
            if (!rules.TaskPriority.ContainsKey(task))
                throw new InvalidOperationException($"{path}: ontbrekende taakprioriteit {task}.");
        foreach (string research in new[] { "carts", "rotation" })
            if (rules.ResearchCosts.GetValueOrDefault(research) <= 0)
                throw new InvalidOperationException($"{path}: ongeldige onderzoekskosten voor {research}.");
        return rules;
    }
    private static Rules Read(string path)
    {
        try
        {
            var rules = JsonSerializer.Deserialize<Rules>(FileAccess.GetFileAsString(path));
            if (rules == null || rules.Goods == null || rules.Buildings == null || rules.ResearchCosts == null ||
                rules.TaskPriority == null || rules.StartingStock == null ||
                rules.Goods.Values.Any(good => good == null || string.IsNullOrWhiteSpace(good.Label)) ||
                rules.Buildings.Values.Any(type => type == null || type.Cost == null || string.IsNullOrWhiteSpace(type.Label)))
                throw new InvalidOperationException($"{path}: ongeldige of lege speldata.");
            return rules;
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"{path}: {error.Message}", error);
        }
    }

    private static void Validate(Dictionary<string, BuildingData> buildings, Dictionary<string, Goods> registeredGoods, string path)
    {
        foreach (var (id, building) in buildings)
        {
            var goods = building.Cost.Keys.Concat(new[] { building.Input, building.Output }.OfType<string>());
            foreach (var good in goods)
                if (!registeredGoods.ContainsKey(good))
                    throw new InvalidOperationException($"{path}: {id} verwijst naar onbekend goed {good}.");
            if (building.Cost.Values.Any(amount => amount <= 0) ||
                (id != "stockyard" && building.BuildSeconds <= 0) ||
                (building.Output != null && (building.Seconds <= 0 || building.Yield <= 0)) ||
                ((building.Output != null || building.Input != null) && building.Capacity <= 0) || building.Housing < 0 ||
                (building.Near != null && !Enum.TryParse<Terrain>(building.Near, out _)))
                throw new InvalidOperationException($"{path}: ongeldige gebouwdata voor {id}.");
        }
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
    public Building? UpgradeTarget { get; init; }
    public bool Expanded { get; set; }
    public int Capacity => Data.Capacity + (Expanded ? 4 : 0);
    public bool Supplied => Data.Cost.All(cost => Delivered.GetValueOrDefault(cost.Key) >= cost.Value);
    public bool Complete => BuildProgress >= Data.BuildSeconds;
}

internal sealed record Delivery(Building Source, Building Destination, string Good, string Task, int Quantity);

internal class Walker(Vector2I home)
{
    public Vector2I Tile { get; private set; } = home;
    public Vector2 Visual { get; private set; } = home;
    private readonly Queue<Vector2I> route = new();
    public IEnumerable<Vector2I> RemainingRoute => route;
    private float progress;

    public void SetRoute(List<Vector2I> path)
    {
        route.Clear();
        foreach (var tile in path.Skip(1)) route.Enqueue(tile);
    }

    public bool Arrived(float seconds, HashSet<Vector2I> paved)
    {
        if (!route.TryPeek(out var next)) return true;
        progress += seconds * (paved.Contains(next) ? 7.5f : 4.5f);
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
    public const float Step = 1f / 30;
    public long TickNumber { get; private set; }
    public List<SessionAction> Actions { get; } = new();
    public const int Width = 17;
    public const int Height = 11;
    public static readonly Vector2I Depot = new(4, 5);
    private static readonly Vector2I[] Directions = { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up };
    public Rules Rules { get; }
    public HashSet<Vector2I> Roads { get; } = new() { Depot + Vector2I.Right };
    public HashSet<Vector2I> PavedRoads { get; } = new();
    public List<Building> Buildings { get; } = new();
    public List<Carrier> Carriers { get; } = new() { new(Depot + Vector2I.Right), new(Depot + Vector2I.Right) };
    public Builder[] Builders { get; } = { new(Depot + Vector2I.Right) };
    public Building Warehouse { get; }
    public bool Campaign { get; }
    public HashSet<string> Technologies { get; } = new();
    public bool FirstPlank { get; private set; }
    public int Population { get; private set; } = 3;
    public int Housing => 3 + Buildings.Where(building => building.Complete && building.Data.Housing > 0 && Connected(building))
        .Sum(building => building.Data.Housing);
    public int FedAtOutpost { get; private set; }
    public int BestSupplyStreak { get; private set; }
    public bool VillageHungry { get; private set; }
    public bool TroopHungry { get; private set; }
    public bool TroopStationed { get; private set; }
    public Building? Garrison { get; private set; }
    public Walker? Troop { get; private set; }
    private float mealClock;
    private float troopClock;
    private float growthClock;
    private const float MealInterval = 12;
    public float NextTroopMeal => Math.Max(0, MealInterval - troopClock);
    public bool Won => FirstPlank && (!Campaign || Population >= 4 && BestSupplyStreak >= 3);

    public Village(Rules rules, bool campaign = false)
    {
        Campaign = campaign;
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
    public Building? At(Vector2I tile) => Buildings.LastOrDefault(building =>
        building.Position == tile && (building.Kind != "expand" || !building.Complete));
    private static IEnumerable<Vector2I> Neighbors(Vector2I tile) => Directions.Select(direction => tile + direction);
    public bool Walkable(Vector2I tile) => Roads.Contains(tile) || Ground(tile) == Terrain.Bridge && Inside(tile);
    private IEnumerable<Vector2I> Access(Building building) => building.Kind == "paved"
        ? new[] { building.Position }
        : Neighbors(building.Position).Where(Walkable);

    public string? Place(string tool, Vector2I tile, bool preview = false)
    {
        if (!Inside(tile)) return "Buiten de kaart.";
        var existing = At(tile);
        if (tool == "road" && (Walkable(tile) || existing?.Kind == "road")) return null;
        if (tool == "expand")
        {
            if (existing == null || !existing.Complete || existing.Expanded || existing.Data.Capacity <= 0)
                return "Kies een voltooid gebouw met opslag dat nog niet is uitgebreid.";
            if (!preview) AddSite(tool, tile, existing);
            return null;
        }
        if (tool == "paved")
        {
            if (!Roads.Contains(tile) || existing?.Kind == "paved")
                return "Kies een afgewerkte onverharde weg.";
            if (!preview)
            {
                if (existing != null) Buildings.Remove(existing);
                AddSite(tool, tile);
            }
            return null;
        }
        if (Ground(tile) != Terrain.Grass || existing != null || Roads.Contains(tile))
            return "Kies een vrije grastegel.";
        var data = Rules.Buildings[tool];
        if (data.Near is string near && !Neighbors(tile).Any(next => Inside(next) && Ground(next).ToString() == near))
            return near == "Forest" ? "Plaats het kamp naast een bosvak." : "Plaats de groeve naast een rotsvak.";
        if (!preview) AddSite(tool, tile);
        return null;
    }

    private void AddSite(string tool, Vector2I tile, Building? upgradeTarget = null)
    {
        Buildings.Add(new Building(tool, tile, Rules.Buildings[tool]) { UpgradeTarget = upgradeTarget });
        Actions.Add(new SessionAction(TickNumber, tool, tile.X, tile.Y));
    }

    private List<Vector2I>? FindRoute(Vector2I start, IEnumerable<Vector2I> goals)
    {
        var targets = goals.ToHashSet();
        if (targets.Count == 0) return null;
        var previous = new Dictionary<Vector2I, Vector2I> { [start] = start };
        var distances = new Dictionary<Vector2I, int> { [start] = 0 };
        var frontier = new PriorityQueue<Vector2I, (int Cost, int Order)>();
        int order = 0;
        frontier.Enqueue(start, (0, order++));
        while (frontier.TryDequeue(out var tile, out var priority))
        {
            if (priority.Cost != distances[tile]) continue;
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
            {
                if (!Walkable(next)) continue;
                int distance = priority.Cost + (PavedRoads.Contains(next) ? 3 : 5);
                if (distances.TryGetValue(next, out int best) && distance >= best) continue;
                distances[next] = distance;
                previous[next] = tile;
                frontier.Enqueue(next, (distance, order++));
            }
        }
        return null;
    }

    private readonly HashSet<Vector2I> connectedRoads = new() { Depot + Vector2I.Right };
    private void RefreshConnectedRoads()
    {
        var frontier = new Queue<Vector2I>();
        var start = Depot + Vector2I.Right;
        connectedRoads.Clear();
        connectedRoads.Add(start);
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var tile))
            foreach (var next in Neighbors(tile))
                if (Walkable(next) && connectedRoads.Add(next)) frontier.Enqueue(next);
    }
    public bool ConnectedRoad(Vector2I tile) => connectedRoads.Contains(tile);
    public bool Connected(Building building) => Access(building).Any(connectedRoads.Contains);

    public IEnumerable<Vector2I> AdjacentTiles(Vector2I tile) => Neighbors(tile).Where(Inside);

    private int Demand(Building building, string good)
    {
        if (!building.Complete)
            return building.Data.Cost.GetValueOrDefault(good) - building.Delivered.GetValueOrDefault(good);
        if (building == Warehouse)
            return good == "base:bread" ? 6 - building.Stock.GetValueOrDefault(good) : 0;
        if (building.Kind == "outpost" && building != Garrison) return 0;
        return building.Data.Input == good ? building.Capacity - building.Stock.GetValueOrDefault(good) : 0;
    }

    private string DeliveryTask(Building destination) => !destination.Complete ? "construction"
        : destination == Warehouse ? "village" : destination == Garrison ? "outpost" : "production";

    public string? Deploy(Building target)
    {
        if (target.Kind != "outpost" || !target.Complete) return "Selecteer een voltooide buitenpost.";
        if (Troop != null) return "De troep is al onderweg of gelegerd.";
        var home = Depot + Vector2I.Right;
        var route = FindRoute(home, Access(target));
        if (route == null) return "Verbind de buitenpost eerst met het wegennet.";
        Garrison = target;
        Troop = new Walker(home);
        Troop.SetRoute(route);
        Actions.Add(new SessionAction(TickNumber, "deploy", target.Position.X, target.Position.Y));
        return null;
    }

    public string? Research(string technology)
    {
        if (!Rules.ResearchCosts.TryGetValue(technology, out int cost)) return "Onbekend onderzoek.";
        if (Technologies.Contains(technology)) return "Dit onderzoek is al afgerond.";
        var workshops = Buildings.Where(building => building.Kind == "research" && building.Complete).ToArray();
        if (workshops.Sum(building => building.Stock.GetValueOrDefault("base:knowledge")) < cost)
            return "De werkplaats heeft nog niet genoeg kennis verzameld.";
        foreach (var workshop in workshops)
        {
            int spend = Math.Min(cost, workshop.Stock.GetValueOrDefault("base:knowledge"));
            workshop.Stock["base:knowledge"] = workshop.Stock.GetValueOrDefault("base:knowledge") - spend;
            cost -= spend;
            if (cost == 0) break;
        }
        Technologies.Add(technology);
        Actions.Add(new SessionAction(TickNumber, technology, 0, 0));
        return null;
    }

    public int Knowledge => Buildings.Where(building => building.Kind == "research")
        .Sum(building => building.Stock.GetValueOrDefault("base:knowledge"));

    private void ConsumeFood(float seconds)
    {
        if (Troop is { } troop && !TroopStationed)
            TroopStationed = troop.Arrived(seconds, PavedRoads);
        if (TroopStationed && Garrison is { } post)
        {
            troopClock += seconds;
            if (troopClock >= MealInterval)
            {
                troopClock -= MealInterval;
                TroopHungry = !TakeBread(post, 1);
                FedAtOutpost = TroopHungry ? 0 : FedAtOutpost + 1;
                BestSupplyStreak = Math.Max(BestSupplyStreak, FedAtOutpost);
            }
        }

        mealClock += seconds;
        if (mealClock >= MealInterval)
        {
            mealClock -= MealInterval;
            VillageHungry = !TakeBread(Warehouse, (Population + 2) / 3);
        }

        if (Population >= Housing || Warehouse.Stock.GetValueOrDefault("base:bread") < 2)
        {
            growthClock = 0;
            return;
        }
        growthClock += seconds;
        if (growthClock < 6) return;
        TakeBread(Warehouse, 2);
        growthClock = 0;
        Population++;
        Carriers.Add(new Carrier(Depot + Vector2I.Right));
    }

    private static bool TakeBread(Building source, int quantity)
    {
        int available = source.Stock.GetValueOrDefault("base:bread");
        if (available < quantity) return false;
        source.Stock["base:bread"] = available - quantity;
        return true;
    }

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
                int pendingPickup = Carriers.Where(other => !other.Loaded &&
                    other.Delivery is { } trip && trip.Source == source && trip.Good == good)
                    .Sum(other => other.Delivery!.Quantity);
                if (quantity <= pendingPickup) continue;
                foreach (var destination in Buildings)
                {
                    if (destination == source) continue;
                    int incoming = Carriers.Where(other =>
                        other.Delivery is { } trip && trip.Destination == destination && trip.Good == good)
                        .Sum(other => other.Delivery!.Quantity);
                    int amount = Math.Min(Demand(destination, good) - incoming, quantity - pendingPickup);
                    amount = Math.Min(amount, Technologies.Contains("carts") ? 2 : 1);
                    if (amount > 0 && FindRoute(pickup[^1], Access(destination)) != null)
                        yield return new Delivery(source, destination, good, DeliveryTask(destination), amount);
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

        if (!carrier.Arrived(seconds, PavedRoads)) return;

        var trip = carrier.Delivery;
        if (!carrier.Loaded)
        {
            trip.Source.Stock[trip.Good] -= trip.Quantity;
            carrier.Loaded = true;
            carrier.SetRoute(FindRoute(carrier.Tile, Access(trip.Destination))!);
            return;
        }

        var inventory = trip.Task == "construction" ? trip.Destination.Delivered : trip.Destination.Stock;
        inventory[trip.Good] = inventory.GetValueOrDefault(trip.Good) + trip.Quantity;
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

        if (builder.Site is not Building target || !builder.Arrived(seconds, PavedRoads)) return;
        builder.Working = true;
        target.BuildProgress = Math.Min(target.BuildProgress + seconds, target.Data.BuildSeconds);
        if (!target.Complete) return;
        if (target.Kind == "road")
        {
            Roads.Add(target.Position);
            RefreshConnectedRoads();
        }
        if (target.Kind == "paved") PavedRoads.Add(target.Position);
        if (target.UpgradeTarget is { } upgraded) upgraded.Expanded = true;
        builder.Site = null;
        builder.Working = false;
    }

    public void Tick(float seconds)
    {
        foreach (var building in Buildings)
        {
            var data = building.Data;
            if (!building.Complete || data.Output is not string output || !Connected(building)) continue;
            if (building.Stock.GetValueOrDefault(output) >= building.Capacity ||
                (data.Input is string input && building.Stock.GetValueOrDefault(input) == 0)) continue;
            building.Progress += seconds;
            if (building.Progress < data.Seconds) continue;
            building.Progress -= data.Seconds;
            if (data.Input is string ingredient) building.Stock[ingredient]--;
            int cropBonus = building.Kind == "farm" && Technologies.Contains("rotation") ? 1 : 0;
            building.Stock[output] = Math.Min(building.Capacity,
                building.Stock.GetValueOrDefault(output) + data.Yield + cropBonus);
            if (building.Kind == "sawmill") FirstPlank = true;
        }
        foreach (var carrier in Carriers) Move(carrier, seconds);
        foreach (var builder in Builders) Build(builder, seconds);
        ConsumeFood(seconds);
        TickNumber++;
    }
}
