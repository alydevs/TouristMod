using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
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
    private readonly IGameGui _gameGui;
    private readonly MarkerService _markerService;
    private readonly PluginConfig _pluginConfig;
    private readonly FFXIVWeatherLuminaService _weatherLuminaService;
    private readonly ExcelSheet<Weather> _weatherSheet;
    private readonly ICommandManager _commandManager;
    private readonly ReadOnlyDictionary<uint, uint> _territoryToAetherCurrentCompFlgSet;

    private static bool _arrVistasExpanded;
    private static DateTime _arrVistasExpandedDT;
    private unsafe static bool ARRVistasExpanded
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

    public MainWindow(
        IClientState clientState,
        IDataManager dataManager,
        IGameGui gameGui,
        MarkerService markerService,
        PluginConfig pluginConfig,
        FFXIVWeatherLuminaService weatherLuminaService,
        ConfigurationLoaderService configurationLoaderService,
        ExcelSheet<Adventure> adventureSheet,
        ExcelSheet<Weather> weatherSheet,
        ExcelSheet<TerritoryType> territoryType,
        ICommandManager commandManager) : base("Tourist##MainWindow", ImGuiWindowFlags.MenuBar)
    {
        _dataManager = dataManager;
        _pluginConfig = pluginConfig;
        _clientState = clientState;
        _gameGui = gameGui;
        _weatherLuminaService = weatherLuminaService;
        _markerService = markerService;
        _configurationLoaderService = configurationLoaderService;
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
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawMenuBar();

        var adventures = GetAdventures();

        int currentLvl = 1;
        foreach (var group in adventures)
        {
            if (group.First().row.MinLevel != currentLvl)
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
    }

    private void DrawMenuBar()
    {
        using var menuBar = ImRaii.MenuBar();
        if (!menuBar)
            return;

        DrawOptionsMenu();
        DrawHelpMenu();
    }

    private void DrawOptionsMenu()
    {
        using var menu = ImRaii.Menu("Options");
        if (!menu)
            return;

        DrawSortByMenu();
        DrawTimesMenu();
        DrawVisibilityMenu();
        DrawArrVistasMenuItem();
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
                colour = new Vector4(0.8f, 0.8f, 0.8f, 1.0f);
            }
            else if (available)
            {
                colour = new Vector4(0f, 1f, 0f, 1f);
                if (_pluginConfig.ShowTimeLeft)
                {
                    countdown = availability?.end;
                }
            }
            else if (availability.HasValue && _pluginConfig.ShowTimeUntilAvailable)
            {
                countdown = availability.Value.start;
            }
            if (blocked)
                colour = ImGuiColors.DalamudRed;

            var next = countdown.HasValue ? $" ({(countdown.Value - DateTimeOffset.UtcNow).ToHumanReadable()})" : string.Empty;

            var name = adventure.Name.ToDalamudString();
            using (ImRaii.PushColor(ImGuiCol.Text, colour.GetValueOrDefault(), colour != null))
                if (!ImGui.CollapsingHeader($"#{idx + 1:000} - {name.TextValue}{next}###adventure-{adventure.RowId}"))
                    continue;

            using (var table = ImRaii.Table("table", 2))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed,
                        ImGui.CalcTextSize("Eorzea time").X + ImGui.GetStyle().ItemSpacing.X * 2);
                    ImGui.TableSetupColumn("Value");

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted("Command");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(adventure.Emote.ValueNullable?.TextCommand.ValueNullable?.Command.ExtractText() ?? "<unk>");

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

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted("Weather");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(Weathers.WeatherString(adventure.RowId, _dataManager));
                }
            }

            var map = adventure.Level.Value.Map.Value;
            var territory = map.TerritoryType.Value;
            var worldPos = new Vector3(adventure.Level.Value.X, adventure.Level.Value.Y, adventure.Level.Value.Z);
            if (ImGui.Button("Open map"))
                _gameGui.OpenMapWithMapLink(territory.RowId, map.RowId, worldPos);
            ImGui.SameLine();
            if (ImGui.Button("Navigate to"))
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
                ImGui.SetTooltip("Currently, mounting is not done automatically. Mount up to start moving.");
            ImGui.SameLine();
            if (ImGui.Button("Stop vnav"))
                _commandManager.ProcessCommand("/vnav stop");
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
