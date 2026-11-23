using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

internal sealed record SessionAction(long Tick, string Tool, int X, int Y);
internal sealed record SessionData(int Version, string Rules, bool Campaign, long Tick, List<SessionAction> Actions);

internal static class SessionSave
{
    public const string Path = "user://settlement-save.json";
    private const int Version = 1;
    private const long MaxTicks = 30L * 60 * 60 * 6;

    private static string Signature(Rules rules) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rules)));

    public static Error Write(Village village)
    {
        if (village.TickNumber > MaxTicks) return Error.ParameterRangeError;
        const string temp = Path + ".tmp";
        using (var file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (file == null) return FileAccess.GetOpenError();
            var state = new SessionData(Version, Signature(village.Rules), village.Campaign, village.TickNumber, village.Actions);
            file.StoreString(JsonSerializer.Serialize(state));
            file.Flush();
            if (file.GetError() != Error.Ok) return file.GetError();
        }
        return DirAccess.RenameAbsolute(temp, Path);
    }

    public static Village Read(Rules rules)
    {
        try
        {
            var state = JsonSerializer.Deserialize<SessionData>(FileAccess.GetFileAsString(Path));
            if (state == null || state.Version != Version || state.Actions == null ||
                state.Tick < 0 || state.Tick > MaxTicks || state.Rules != Signature(rules))
                throw new InvalidOperationException("Dit opgeslagen spel past niet bij de huidige speldata of mods.");
            var village = new Village(rules, state.Campaign);
            foreach (var action in state.Actions)
            {
                if (action == null || string.IsNullOrEmpty(action.Tool) || action.Tick < village.TickNumber || action.Tick > state.Tick)
                    throw new InvalidOperationException("Het opgeslagen spel bevat een ongeldige volgorde.");
                while (village.TickNumber < action.Tick) village.Tick(Village.Step);
                var tile = new Vector2I(action.X, action.Y);
                string? error = action.Tool == "deploy" && village.At(tile) is { } post
                    ? village.Deploy(post)
                    : rules.ResearchCosts.ContainsKey(action.Tool) ? village.Research(action.Tool)
                    : rules.Buildings.ContainsKey(action.Tool) && action.Tool != "stockyard"
                        ? village.Place(action.Tool, tile) : "Onbekende opdracht.";
                if (error != null) throw new InvalidOperationException($"Bouwopdracht in opgeslagen spel mislukt: {error}");
            }
            while (village.TickNumber < state.Tick) village.Tick(Village.Step);
            return village;
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Het opgeslagen spel kon niet worden gelezen.", error);
        }
    }
}
