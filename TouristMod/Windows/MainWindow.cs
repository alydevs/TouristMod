using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVWeather.Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using TouristMod.Config;
using TouristMod.Services;
using TouristMod.Util;

namespace TouristMod.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly ExcelSheet<Adventure> _adventureSheet;
    private readonly ExcelSheet<TerritoryType> _territoryType;
    private readonly IClientState _clientState;
    private readonly ConfigurationLoaderService _configurationLoaderService;
    private readonly IDataManager _dataManager;
    private readonly IObjectTable _objectTable;
    private readonly IGameGui _gameGui;
    private readonly MarkerService _markerService;
    private readonly PluginConfig _pluginConfig;
    private readonly FFXIVWeatherLuminaService _weatherLuminaService;
    private readonly ExcelSheet<Weather> _weatherSheet;
    private readonly ICommandManager _commandManager;
    private readonly NotificationSchedulerService _notificationScheduler;
    private readonly ReadOnlyDictionary<uint, uint> _territoryToAetherCurrentCompFlgSet;

    private static (int idx, float distance) _closest = (0, float.MaxValue);
    public MainWindow(
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IGameGui gameGui,
        MarkerService markerService,
        PluginConfig pluginConfig,
        FFXIVWeatherLuminaService weatherLuminaService,
        ConfigurationLoaderService configurationLoaderService,
        NotificationSchedulerService notificationScheduler,
        ExcelSheet<Adventure> adventureSheet,
        ExcelSheet<Weather> weatherSheet,
        ExcelSheet<TerritoryType> territoryType,
        ICommandManager commandManager) : base("Tourist##MainWindow", ImGuiWindowFlags.MenuBar)
    {
        _dataManager = dataManager;
        _pluginConfig = pluginConfig;
        _clientState = clientState;
        _objectTable = objectTable;
        _gameGui = gameGui;
        _weatherLuminaService = weatherLuminaService;
        _markerService = markerService;
        _configurationLoaderService = configurationLoaderService;
        _notificationScheduler = notificationScheduler;
        _adventureSheet = adventureSheet;
        _weatherSheet = weatherSheet;
        _commandManager = commandManager;
        _territoryType = territoryType;
        _territoryToAetherCurrentCompFlgSet = _territoryType
            .Where(x => x.RowId > 0 && x.AetherCurrentCompFlgSet.RowId > 0)
            .ToDictionary(x => x.RowId, x => x.AetherCurrentCompFlgSet.RowId)
            .AsReadOnly();

        Size = new Vector2(350, 450);
        SizeCondition = ImGuiCond.FirstUseEver;
        if (_pluginConfig.DefaultOpen)
            IsOpen = true;
    }

    private static bool _arrVistasExpanded;
    private static DateTime _arrVistasExpandedDT;
    internal unsafe static bool ARRVistasExpanded
    {
        get
        {
            if ((DateTime.Now - _arrVistasExpandedDT).TotalSeconds > 60)
            {
                var playerState = PlayerState.Instance();
                _arrVistasExpanded = playerState != null && Enumerable.Range(0, 20).All(idx => playerState->IsAdventureComplete((uint)idx));
                _arrVistasExpandedDT = DateTime.Now;
            }
            return _arrVistasExpanded;
        }
    }

    private readonly static Dictionary<int, Vector3> locationOverrides = new() {
        { 104, new(867.34906f, 47.032375f, -32.1302f) }, // Voor Sian Siran
        { 117, new(543.2363f, 219.76675f, 652.7301f) }, // fractal continuum
        { 133, new(-392.68234f, 113.04094f, 122.56957f) }, // The Old Father
        { 140, new(-595.59674f, -169.00003f, -366.43912f) }, // Centrifugal Crystal Engine
        { 153, new(181.46855f, 165.69328f, -782.3936f) }, // Ala Gannha
        { 162, new(678.4738f, 70f, 512.5864f) }, // hidden tunnel
        { 163, new(-777.7409f, 240.90013f, 28.044926f) }, // Porta Praetoria
        { 172, new(506.11508f, 58.843567f, 790.02814f) }, // Sakazuki
        { 179, new(-325.8019f, 94.82681f, -755.7113f) }, // Doma Castle
        { 181, new(-312.05276f, 59.25094f, 507.4568f) }, // Yuzuka Manor
        { 201, new(1.2207426f, 33.522175f, -474.66684f) }, // Crick
        { 209, new(-192.23924f, 35.58688f, -77.07811f) }, // Temenos Rookery
        { 210, new(44.42447f, -2.6557508f, -128.63736f) }, // glory gate
        { 212, new(-10.925671f, 48.05f, -2.1686547f) }, // eulmoran army hq
        { 227, new(587.98975f, -42.814953f, -384.24127f) }, // Red Serai
        { 239, new(-390.17166f, 38.664627f, 548.02734f) }, // fort gohn
        { 241, new(-854.3621f, -82.97393f, 290.2913f) }, // covered halls
        { 251, new(34.487827f, -16.146997f, 228.52016f) }, // scholars
        { 254, new(0.30054197f, 2.5105362f, -53.76743f) }, // rostra
        { 270, new(53.078583f, 117.62871f, -91.06518f) }, // Kadjaya
    };

    private readonly static Dictionary<int, string> comments = new() {
        { 140, "vnav couldn't fly inside this structure; the vista is inside on the left wall" },
        { 162, "Talk to npc, vista is inside tunnel, between crates on the right before the large spiral stairwell" },
        { 212, "Big jump. The chain is solid fyi." }, // eulmoran army hq
        { 265, "Can't fly in here, make sure you stop navigation before going through the door" },
    };


    public void Dispose() { }

    public override void Draw()
    {
        DrawMenuBar();

        var adventures = GetAdventures();

        int currentLvl = 1;
        List<int> idxList = [];
        foreach (var group in adventures)
        {
            foreach (var (adventure, idx) in group)
                idxList.Add(idx);
            if (currentLvl != 1 && group.First().row.MinLevel != currentLvl)
                ImGui.Spacing();
            currentLvl = group.First().row.MinLevel;
            if (_pluginConfig.SortMode == SortMode.Zone)
            {
                var zoneName = group.First().row.Level.ValueNullable?.Map.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText()
                    .StripSoftHyphen().FirstCharToUpper();
                if (!ImGui.CollapsingHeader($"{zoneName}##group-{group.Key}"))
                    continue;

                using var indent = ImRaii.PushIndent();
                DrawGroup(group);
            }
            else
            {
                DrawGroup(group);
            }
        }
        if (!idxList.Contains(_closest.idx))
            _closest = (0, float.MaxValue);
    }

    private void DrawMenuBar()
    {
        using var menuBar = ImRaii.MenuBar();
        if (!menuBar)
            return;

        DrawOptionsMenu();
        DrawHelpMenu();
        using var clock = ImRaii.Menu($"{DateUtil.EorzeaTime():H:mm} ET");
    }

    private void DrawOptionsMenu()
    {
        using var menu = ImRaii.Menu("Options");
        if (!menu)
            return;

        DrawSortByMenu();
        DrawTimesMenu();
        DrawVisibilityMenu();
        DrawNotifyMenu();

        var defaultOpen = _pluginConfig.DefaultOpen;
        if (ImGui.MenuItem("Open automatically", ref defaultOpen))
        {
            _pluginConfig.DefaultOpen = defaultOpen;
            _configurationLoaderService.Save();
        }
    }

    private void DrawSortByMenu()
    {
        using var menu = ImRaii.Menu("Sort by");
        if (!menu)
            return;

        foreach (var mode in Enum.GetValues<SortMode>())
        {
            if (!ImGui.MenuItem($"{mode}", _pluginConfig.SortMode == mode))
                continue;

            _pluginConfig.SortMode = mode;
            _configurationLoaderService.Save();
        }
    }

    private void DrawTimesMenu()
    {
        using var menu = ImRaii.Menu("Times");
        if (!menu)
            return;

        var showTimeUntilAvailable = _pluginConfig.ShowTimeUntilAvailable;
        if (ImGui.MenuItem("Show time until available", ref showTimeUntilAvailable))
        {
            _pluginConfig.ShowTimeUntilAvailable = showTimeUntilAvailable;
            _configurationLoaderService.Save();
        }

        var showTimeLeft = _pluginConfig.ShowTimeLeft;
        if (ImGui.MenuItem("Show time left", ref showTimeLeft))
        {
            _pluginConfig.ShowTimeLeft = showTimeLeft;
            _configurationLoaderService.Save();
        }
    }

    private void DrawVisibilityMenu()
    {
        using var menu = ImRaii.Menu("Visibility");
        if (!menu)
            return;
        var showFinished = _pluginConfig.ShowFinished;
        if (ImGui.MenuItem("Show finished", ref showFinished))
        {
            _pluginConfig.ShowFinished = showFinished;
            _configurationLoaderService.Save();
        }

        var showUnavailable = _pluginConfig.ShowUnavailable;
        if (ImGui.MenuItem("Show unavailable", ref showUnavailable))
        {
            _pluginConfig.ShowUnavailable = showUnavailable;
            _configurationLoaderService.Save();
        }

        var onlyShowCurrentZone = _pluginConfig.OnlyShowCurrentZone;
        if (ImGui.MenuItem("Show current zone only", ref onlyShowCurrentZone))
        {
            _pluginConfig.OnlyShowCurrentZone = onlyShowCurrentZone;
            _configurationLoaderService.Save();
        }

        var showBlocked = _pluginConfig.ShowBlocked;
        if (ImGui.MenuItem("Show inaccessible", ref showBlocked))
        {
            _pluginConfig.ShowBlocked = showBlocked;
            _configurationLoaderService.Save();
        }
        DrawArrVistasMenuItem();
    }

    private void DrawNotifyMenu()
    {
        var notify = _pluginConfig.Notify;
        if (ImGui.MenuItem("Notify 60 seconds before", ref notify))
        {
            _pluginConfig.Notify = notify;
            _configurationLoaderService.Save();
        }
    }

    private void DrawArrVistasMenuItem()
    {
        var showArrVistas = _pluginConfig.ShowArrVistas;
        if (!ImGui.MenuItem("Add markers for ARR vistas", ref showArrVistas))
            return;

        _pluginConfig.ShowArrVistas = showArrVistas;
        _configurationLoaderService.Save();

        if (showArrVistas)
        {
            _markerService.SpawnVfxForCurrentZone();
        }
        else
        {
            _markerService.RemoveAllVfx();
        }
    }

    private void DrawHelpMenu()
    {
        using var menu = ImRaii.Menu("Help");
        if (!menu)
            return;

        using var vistaUnlockMenu = ImRaii.Menu("Can't unlock vistas 21 to 80");
        if (!vistaUnlockMenu)
            return;

        using var textWrapPos = ImRaii.TextWrapPos(ImGui.GetFontSize() * 10);
        ImGui.TextUnformatted(
            "Vistas 21 to 80 require the completion of the first 20. Talk to Millith Ironheart in Old Gridania to unlock the rest.");

    }

    private unsafe void DrawGroup(IGrouping<uint, (Adventure row, int idx)> group)
    {
        foreach (var (adventure, idx) in group)
        {
            bool blocked = false;
            if (idx >= 20 && idx < 80 && !ARRVistasExpanded)
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (idx >= 80 && idx < 142 && !QuestManager.IsQuestComplete(2107))
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (idx >= 142 && idx < 204 && !QuestManager.IsQuestComplete(2920))
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (idx >= 204 && idx < 250 && !QuestManager.IsQuestComplete(3604))
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (idx >= 250 && idx < 295 && !QuestManager.IsQuestComplete(4174))
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (idx >= 295 && idx < 323 && !QuestManager.IsQuestComplete(5006))
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (idx >= 323 && idx < 340 && !QuestManager.IsQuestComplete(5007))
            {
                if (_pluginConfig.ShowUnavailable)
                    blocked = true;
                else
                    continue;
            }
            if (blocked && !_pluginConfig.ShowBlocked)
                continue;
            using var id = ImRaii.PushId((int)adventure.RowId);

            bool has;
            var playerState = PlayerState.Instance();
            has = playerState != null && playerState->IsAdventureComplete((uint)idx);

            var available = adventure.Available(_weatherLuminaService);
            var availability = adventure.NextAvailable(_weatherLuminaService);

            DateTimeOffset? countdown = null;
            Vector4? colour = null;
            if (has)
            {
                colour = ImGuiColors.DalamudGrey2;
            }
            else if (available)
            {
                colour = ImGuiColors.ParsedGreen;
                if (_pluginConfig.ShowTimeLeft)
                {
                    countdown = availability?.end;
                }
            }
            else if (_pluginConfig.ShowTimeUntilAvailable)
            {
                countdown = availability?.start;
            }
            if (blocked)
                colour = ImGuiColors.DalamudRed;
            if (colour == ImGuiColors.ParsedGreen && _closest.idx == idx)
                colour = ImGuiColors.DalamudOrange;

            var next = countdown.HasValue ? $" ({(countdown.Value - DateTimeOffset.UtcNow).ToHumanReadable()})" : string.Empty;

            var name = adventure.Name.ToDalamudString();
            var map = adventure.Level.Value.Map.Value;
            var territory = map.TerritoryType.Value;
            Vector3 worldPos = new(adventure.Level.Value.X, adventure.Level.Value.Y, adventure.Level.Value.Z);
            if (locationOverrides.TryGetValue(idx + 1, out var value))
                worldPos = value;
            if (_objectTable[0] is IGameObject obj && _clientState.TerritoryType == territory.RowId)
            {
                Vector3 difference = obj.Position - worldPos;
                float distance = MathF.Sqrt(difference.X * difference.X + difference.Y * difference.Y + difference.Z * difference.Z);
                next = $" ({distance:0}y){next}";
                if (_closest.idx == 0 || _closest.distance > distance)
                    _closest = (idx, distance);
            }

            if (countdown.HasValue && !has && !blocked)
            {
                var fireAt = countdown.Value - TimeSpan.FromSeconds(60);
                if (fireAt > DateTimeOffset.UtcNow)
                    _notificationScheduler.Schedule(idx, fireAt,
                        $"#{idx + 1:000} {name.TextValue} {(!available ? "is available" : "ends")} in 60 seconds");
            }

            using (ImRaii.PushColor(ImGuiCol.Text, colour.GetValueOrDefault(), colour != null))
                if (!ImGui.CollapsingHeader($"#{idx + 1:000} - {name.TextValue}{next}###adventure-{adventure.RowId}", flags: ImGuiTreeNodeFlags.DefaultOpen))
                    continue;

            if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.Map))
                _gameGui.OpenMapWithMapLink(territory.RowId, map.RowId, worldPos);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show vista on map");
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.MapPin))
            {
                _gameGui.OpenMapWithMapLink(territory.RowId, map.RowId, worldPos);
                if (_clientState.TerritoryType == territory.RowId)
                {
                    if (playerState != null &&
                            _territoryToAetherCurrentCompFlgSet.TryGetValue(territory.RowId, out uint aetherCurrentCompFlgSet) &&
                            playerState->IsAetherCurrentZoneComplete(aetherCurrentCompFlgSet))
                        _commandManager.ProcessCommand($"/vnav flyto {worldPos.X} {worldPos.Y} {worldPos.Z}");
                    else
                        _commandManager.ProcessCommand($"/vnav moveto {worldPos.X} {worldPos.Y} {worldPos.Z}");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fly/walk to location\nCurrently, mounting is not done automatically. Mount up to start moving.");
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.Stop))
                _commandManager.ProcessCommand("/vnav stop");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stop vnav (for if it gets stuck)");

            string? emote = adventure.Emote.ValueNullable?.TextCommand.ValueNullable?.Command.ExtractText();
            bool time = adventure.MinTime != 0 || adventure.MaxTime != 0;
            bool weather = !Weathers.WeatherString(adventure.RowId, _dataManager).Equals("Any");
            string? comment = comments.GetValueOrDefault(idx + 1);
            if ((emote != null && !emote.Equals("/lookout")) || time || weather || comment != null || countdown.HasValue)
            {
                using var table = ImRaii.Table("table", 2);
                if (table)
                {
                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed,
                        ImGui.CalcTextSize("Eorzea time").X + ImGui.GetStyle().ItemSpacing.X * 2);
                    ImGui.TableSetupColumn("Value");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted("Command");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(emote ?? "<unk>");

                    if (adventure.MinTime != 0 || adventure.MaxTime != 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted("Eorzea time");
                        ImGui.TableSetColumnIndex(1);
                        if (adventure.MinTime != 0 || adventure.MaxTime != 0)
                        {
                            ImGui.TextUnformatted($"{adventure.MinTime / 100:00}:00 to {adventure.MaxTime / 100 + 1:00}:00");
                        }
                        else
                        {
                            ImGui.TextUnformatted("Any");
                        }
                    }

                    var weatherString = Weathers.WeatherString(adventure.RowId, _dataManager);
                    if (!weatherString.Equals("Any"))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted("Weather");
                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(Weathers.WeatherString(adventure.RowId, _dataManager));
                    }

                    if (comment != null)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted("Comment");
                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextWrapped(comment);
                    }

                    if (countdown.HasValue)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        if (available)
                            ImGui.TextUnformatted("Next down");
                        else
                            ImGui.TextUnformatted("Next up");
                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextWrapped($"{countdown.Value.ToLocalTime().ToString("G")}");
                    }
                }
            }
        }
    }
    private string WeatherString(uint[] weathers)
    {
        var weatherString = string.Join(", ", weathers
            .OrderBy(id => id)
            .Select(id => _weatherSheet.GetRowOrDefault(id))
            .Where(weather => weather.HasValue && weather.Value.RowId != 0)
            .Select(weather => weather!.Value.Name));
        return weatherString;
    }

    private uint AvailabilitySeconds(Adventure row)
    {
        if (row.NextAvailable(_weatherLuminaService) is (DateTimeOffset, DateTimeOffset) availability)
            return (uint)(DateTime.Now - availability.end).TotalSeconds;
        return 0;
    }

    private IEnumerable<IGrouping<uint, (Adventure row, int idx)>> GetAdventures()
    {
        return _adventureSheet
            .Select((row, idx) => (row, idx))
            .OrderBy(entry => _pluginConfig.SortMode switch
            {
                SortMode.Number => (uint)entry.idx,
                SortMode.Zone => entry.row.Level.Value.Map.RowId,
                _ => throw new ArgumentOutOfRangeException(),
            })
            .Where(ShouldShow)
            .GroupBy(entry => _pluginConfig.SortMode switch
            {
                SortMode.Number => (uint)entry.idx,
                SortMode.Zone => entry.row.Level.Value.Map.RowId,
                _ => throw new ArgumentOutOfRangeException(),
            });
    }

    private bool ShouldShow((Adventure row, int idx) entry)
    {
        if (_pluginConfig.OnlyShowCurrentZone && entry.idx > 80 && entry.row.Level.Value.Territory.RowId != _clientState.TerritoryType)
        {
            return false;
        }

        bool has;
        unsafe
        {
            var playerState = PlayerState.Instance();
            has = playerState != null && playerState->IsAdventureComplete((uint)entry.idx);
        }

        if (!_pluginConfig.ShowFinished && has)
        {
            return false;
        }

        var available = entry.row.Available(_weatherLuminaService);

        return _pluginConfig.ShowUnavailable || available;
    }
}
