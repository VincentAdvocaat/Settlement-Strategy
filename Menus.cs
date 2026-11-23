using System;
using System.Linq;
using Godot;

public partial class Main
{
    private enum Menu { None, Title, Pause, Options, Controls, Research, DisplayConfirm, Leave }
    private Menu currentMenu;
    private Menu optionsParent;
    private Control? overlay;
    private VBoxContainer menuContent = null!;
    private Label? countdown;
    private GameOptions options = GameOptions.Default;
    private GameOptions draft = GameOptions.Default;
    private double confirmRemaining;
    private AudioStreamPlayer cue = null!;
    private AudioStream clickSound = null!;
    private AudioStream completeSound = null!;
    private const string EffectsBus = "Effecten";

    private int GuiFont(int size) => Mathf.RoundToInt(size * options.GuiPercent / 100f);

    private void InitMenus()
    {
        if (AudioServer.GetBusIndex(EffectsBus) < 0)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, EffectsBus);
        }
        cue = new AudioStreamPlayer { Bus = EffectsBus };
        AddChild(cue);
        clickSound = Tone(420, 0.09f);
        completeSound = Tone(660, 0.18f);
        options = ApplyOptions(GameOptions.Load(), true);
        ScaleHud();
        ShowMenu(Menu.Title);
    }

    private static AudioStreamWav Tone(float hz, float duration)
    {
        const int rate = 22050;
        byte[] samples = new byte[(int)(rate * duration) * 2];
        for (int i = 0; i < samples.Length / 2; i++)
        {
            float time = i / (float)rate;
            float envelope = Math.Min(time / 0.009f, 1) * Math.Max(0, 1 - time / duration);
            short sample = (short)(Math.Sin(Math.Tau * hz * time) * envelope * 5200);
            samples[i * 2] = (byte)sample;
            samples[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = samples };
    }

    private void PlayCue(AudioStream sound)
    {
        cue.Stream = sound;
        cue.Play();
    }

    private static void Volume(int bus, int percent)
    {
        AudioServer.SetBusMute(bus, percent == 0);
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Math.Max(percent, 1) / 100f));
    }

    private Vector2I[] AvailableResolutions()
    {
        var usable = DisplayServer.ScreenGetUsableRect(GetWindow().CurrentScreen).Size;
        var sizes = GameOptions.Resolutions.Where(size => size.X <= usable.X && size.Y + 45 <= usable.Y).ToArray();
        return sizes.Length == 0 ? new[] { GameOptions.Resolutions[0] } : sizes;
    }

    private GameOptions ApplyOptions(GameOptions value, bool changeWindow)
    {
        if (changeWindow)
        {
            var available = AvailableResolutions();
            if (!available.Contains(value.Resolution))
                value = value with { Resolution = available.Last() };
            var window = GetWindow();
            window.Mode = value.Fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
            if (!value.Fullscreen)
            {
                window.Size = value.Resolution;
                var usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
                window.Position = usable.Position + (usable.Size - value.Resolution) / 2;
            }
        }
        DisplayServer.WindowSetVsyncMode(value.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = value.Fps;
        Volume(0, value.Master);
        Volume(AudioServer.GetBusIndex(EffectsBus), value.Effects);
        return value;
    }

    private void UpdateMenus(double delta)
    {
        if (currentMenu != Menu.DisplayConfirm) return;
        confirmRemaining -= delta;
        if (confirmRemaining <= 0)
        {
            RevertDisplay();
            return;
        }
        if (countdown != null) countdown.Text = $"Automatisch terug over {Math.Ceiling(confirmRemaining):0} s";
    }

    private void HandleEscape()
    {
        switch (currentMenu)
        {
            case Menu.None: ShowMenu(Menu.Pause); break;
            case Menu.Pause: ShowMenu(Menu.None); break;
            case Menu.Options: ShowMenu(optionsParent); break;
            case Menu.Controls: ShowMenu(Menu.Options); break;
            case Menu.Research: ShowMenu(Menu.None); break;
            case Menu.DisplayConfirm: RevertDisplay(); break;
            case Menu.Leave: ShowMenu(Menu.Pause); break;
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (currentMenu == Menu.DisplayConfirm) RevertDisplay();
        if (playing) ConfirmLeave("Spel afsluiten?", () => GetTree().Quit());
        else GetTree().Quit();
    }

    private bool playing;
    private Action? leaveAction;
    private string leaveTitle = "";

    private void Begin(bool campaign)
    {
        StartSession(campaign);
        playing = true;
        ShowMenu(Menu.None);
        if (!string.IsNullOrEmpty(loadWarning)) GD.PushWarning(loadWarning);
    }

    private void SaveCurrent()
    {
        string message = SessionSave.Write(village) == Error.Ok ? "Spel opgeslagen." : "Opslaan mislukt.";
        ShowMenu(Menu.Pause);
        MenuLabel(message, 14, Gold);
    }

    private void ContinueSave()
    {
        try
        {
            LoadSession(SessionSave.Read(ReadSessionRules()));
            playing = true;
            ShowMenu(Menu.None);
        }
        catch (InvalidOperationException error)
        {
            ShowMenu(Menu.Title);
            MenuLabel(error.Message, 13, Gold);
        }
    }

    private void ConfirmLeave(string title, Action action)
    {
        leaveTitle = title;
        leaveAction = action;
        ShowMenu(Menu.Leave);
    }

    private void OpenOptions()
    {
        optionsParent = currentMenu;
        draft = options;
        ShowMenu(Menu.Options);
    }

    private void ShowMenu(Menu next)
    {
        currentMenu = next;
        roadAnchor = null;
        ClearRoadPreview();
        if (overlay != null)
        {
            overlay.Hide();
            overlay.QueueFree();
            overlay = null;
        }
        foreach (var control in HudControls) control.Visible = next == Menu.None;
        if (next == Menu.None) return;

        overlay = new Control { Size = new Vector2(1120, 720), MouseFilter = Control.MouseFilterEnum.Stop, ZIndex = 10 };
        AddChild(overlay);
        overlay.AddChild(new ColorRect
        {
            Color = new Color("#111a16ee"), Size = overlay.Size, MouseFilter = Control.MouseFilterEnum.Ignore
        });
        var center = new CenterContainer { Size = overlay.Size };
        overlay.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(next == Menu.Options ? 660 : 520, 0) };
        var surface = new StyleBoxFlat
        {
            BgColor = Panel, BorderColor = new Color("#526759"),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 24, ContentMarginBottom = 24
        };
        panel.AddThemeStyleboxOverride("panel", surface);
        center.AddChild(panel);
        menuContent = new VBoxContainer();
        menuContent.AddThemeConstantOverride("separation", 9);
        panel.AddChild(menuContent);

        switch (next)
        {
            case Menu.Title:
                MenuLabel("Settlement Strategy", 32, Gold);
                MenuLabel("Bouw een dorp en bevoorraad de riviergrens.", 16, Paper);
                MenuButton("Campagne • De riviergrens", () => Begin(true));
                MenuButton("Vrij bouwen", () => Begin(false));
                if (FileAccess.FileExists(SessionSave.Path)) MenuButton("Verder met opgeslagen spel", ContinueSave);
                if (!string.IsNullOrEmpty(loadWarning))
                    MenuLabel("Mod kon niet laden. Details staan in het logboek.", 13, Gold);
                MenuButton("Instellingen", OpenOptions);
                MenuButton("Afsluiten", () => GetTree().Quit());
                break;
            case Menu.Pause:
                MenuLabel("Gepauzeerd", 28, Gold);
                MenuButton("Hervatten", () => ShowMenu(Menu.None));
                MenuButton("Instellingen", OpenOptions);
                MenuButton("Spel opslaan", SaveCurrent);
                MenuButton("Opnieuw beginnen", () => ConfirmLeave("Opnieuw beginnen?", () => { Reset(); ShowMenu(Menu.None); }));
                MenuButton("Hoofdmenu", () => ConfirmLeave("Terug naar het hoofdmenu?", () => { playing = false; ShowMenu(Menu.Title); }));
                MenuButton("Afsluiten", () => ConfirmLeave("Spel afsluiten?", () => GetTree().Quit()));
                break;
            case Menu.Options: DrawOptions(); break;
            case Menu.Controls:
                MenuLabel("Bediening", 28, Gold);
                MenuLabel("Linkermuisknop    Selecteren of plaatsen", 16, Paper);
                MenuLabel("Slepen met Weg    Meerdere wegtegels plannen", 16, Paper);
                MenuLabel("Rechtermuisknop    Terug naar inspecteren", 16, Paper);
                MenuLabel("0–9    Bouwgereedschap", 16, Paper);
                MenuLabel("T    Troep naar geselecteerde buitenpost sturen", 16, Paper);
                MenuLabel("Spatie    Simulatie pauzeren", 16, Paper);
                MenuLabel("Escape    Pauzemenu of terug", 16, Paper);
                MenuButton("Terug", () => ShowMenu(Menu.Options));
                break;
            case Menu.Research:
                MenuLabel("Onderzoek", 28, Gold);
                MenuLabel($"Kennis beschikbaar: {village.Knowledge}", 16, Paper);
                MenuLabel("Een werkplaats zet geleverde planken om in kennis.", 15, Paper);
                AddResearchButton("carts", "Karren • twee goederen per rit");
                AddResearchButton("rotation", "Gewasrotatie • meer graan per oogst");
                MenuButton("Terug", () => ShowMenu(Menu.None));
                break;
            case Menu.DisplayConfirm:
                MenuLabel("Weergave behouden?", 26, Gold);
                countdown = MenuLabel("Automatisch terug over 15 s", 16, Paper);
                MenuButton("Terugdraaien", RevertDisplay);
                MenuButton("Behouden", CommitOptions);
                break;
            case Menu.Leave:
                MenuLabel(leaveTitle, 26, Gold);
                MenuLabel("De voortgang van deze sessie gaat verloren.", 16, Paper);
                MenuButton("Verder spelen", () => ShowMenu(Menu.Pause));
                MenuButton("Bevestigen", () => leaveAction?.Invoke());
                break;
        }
        menuContent.FindNextValidFocus()?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void AddResearchButton(string technology, string description)
    {
        int cost = village.Rules.ResearchCosts[technology];
        bool done = village.Technologies.Contains(technology);
        var button = MenuButton(done ? description + " ✓" : $"{description} ({cost} kennis)", () =>
        {
            var error = village.Research(technology);
            ShowMenu(Menu.Research);
            if (error != null) MenuLabel(error, 14, Gold);
        });
        button.Disabled = done || village.Knowledge < cost;
    }

    private Label MenuLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", GuiFont(size));
        label.AddThemeColorOverride("font_color", color);
        menuContent.AddChild(label);
        return label;
    }

    private Button MenuButton(string text, Action action, Container? parent = null)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, GuiFont(34)) };
        button.AddThemeFontSizeOverride("font_size", GuiFont(16));
        button.Pressed += () => { PlayCue(clickSound); action(); };
        (parent ?? menuContent).AddChild(button);
        return button;
    }

    private HBoxContainer SettingRow(string text)
    {
        var row = new HBoxContainer();
        menuContent.AddChild(row);
        var label = new Label { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", GuiFont(15));
        row.AddChild(label);
        return row;
    }

    private OptionButton Choice(string name, string[] labels, int selected, Action<int>? change = null)
    {
        var choice = new OptionButton { CustomMinimumSize = new Vector2(235, GuiFont(30)) };
        choice.AddThemeFontSizeOverride("font_size", GuiFont(15));
        choice.GetPopup().AddThemeFontSizeOverride("font_size", GuiFont(15));
        foreach (var label in labels) choice.AddItem(label);
        choice.Select(Math.Max(0, selected));
        if (change != null) choice.ItemSelected += index => change((int)index);
        SettingRow(name).AddChild(choice);
        return choice;
    }

    private void DrawOptions()
    {
        MenuLabel("Instellingen", 28, Gold);
        MenuLabel("Beeld", 16, Gold);
        var resolutions = AvailableResolutions();
        var mode = Choice("Schermmodus", new[] { "Venster", "Fullscreen" }, draft.Fullscreen ? 1 : 0);
        var resolution = Choice("Vensterresolutie", resolutions.Select(size => $"{size.X} × {size.Y}").ToArray(),
            Array.IndexOf(resolutions, draft.Resolution), i => draft = draft with { Resolution = resolutions[i] });
        resolution.Disabled = draft.Fullscreen;
        mode.ItemSelected += i =>
        {
            draft = draft with { Fullscreen = i == 1 };
            resolution.Disabled = draft.Fullscreen;
        };
        Choice("VSync", new[] { "Uit", "Aan" }, draft.VSync ? 1 : 0, i => draft = draft with { VSync = i == 1 });
        Choice("FPS-limiet", GameOptions.FrameLimits.Select(fps => fps == 0 ? "Onbeperkt" : fps.ToString()).ToArray(),
            Array.IndexOf(GameOptions.FrameLimits, draft.Fps), i => draft = draft with { Fps = GameOptions.FrameLimits[i] });
        Choice("GUI-schaal", GameOptions.GuiSizes.Select(scale => $"{scale}%").ToArray(),
            Array.IndexOf(GameOptions.GuiSizes, draft.GuiPercent),
            i => draft = draft with { GuiPercent = GameOptions.GuiSizes[i] });
        MenuLabel("Geluid", 16, Gold);
        Slider("Hoofdvolume", draft.Master, value => draft = draft with { Master = value });
        Slider("Effectvolume", draft.Effects, value => draft = draft with { Effects = value });
        var actions = new HBoxContainer();
        menuContent.AddChild(actions);
        MenuButton("Toepassen", ApplyDraft, actions);
        MenuButton("Standaard", () => { draft = GameOptions.Default; ShowMenu(Menu.Options); }, actions);
        MenuButton("Bediening", () => ShowMenu(Menu.Controls), actions);
        MenuButton("Terug", () => ShowMenu(optionsParent), actions);
    }

    private void Slider(string name, int value, Action<int> change)
    {
        var row = SettingRow(name);
        var slider = new HSlider { MaxValue = 100, Step = 1, Value = value, CustomMinimumSize = new Vector2(175, GuiFont(30)) };
        var number = new Label { Text = $"{value}%", CustomMinimumSize = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Right };
        number.AddThemeFontSizeOverride("font_size", GuiFont(15));
        row.AddChild(slider);
        row.AddChild(number);
        slider.ValueChanged += next =>
        {
            number.Text = $"{next:0}%";
            change((int)next);
        };
    }

    private void ApplyDraft()
    {
        bool displayChanged = draft.Fullscreen != options.Fullscreen || draft.Resolution != options.Resolution;
        draft = ApplyOptions(draft, displayChanged);
        if (!displayChanged)
        {
            CommitOptions();
            return;
        }
        confirmRemaining = 15;
        ShowMenu(Menu.DisplayConfirm);
    }

    private void RevertDisplay()
    {
        draft = ApplyOptions(options, true);
        ShowMenu(Menu.Options);
    }

    private void ScaleHud()
    {
        foreach (var control in HudControls)
        {
            float height = GuiFont(36);
            control.Position = new Vector2(control.Position.X, (control.Position.Y < 300 ? 102 : 626) - height / 2);
            control.Size = new Vector2(control.Size.X, height);
            control.AddThemeFontSizeOverride("font_size", GuiFont(14));
        }
    }

    private void CommitOptions()
    {
        options = draft;
        ScaleHud();
        ShowMenu(Menu.Options);
        if (options.Save() != Error.Ok) MenuLabel("Instellingen konden niet worden opgeslagen.", 14, Gold);
        PlayCue(clickSound);
    }
}
