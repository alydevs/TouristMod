using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVWeather.Lumina;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using TouristMod.Config;
using TouristMod.Util;
using TouristMod.Windows;

namespace TouristMod.Services;

public class MarkerService(
    IClientState clientState,
    PluginConfig pluginConfig,
    IPluginLog pluginLog,
    IDataManager dataManager,
    VfxService vfxService,
    IUnlockState unlockState,
    FFXIVWeatherLuminaService weatherLuminaService)
    : IHostedService
{
    private const string SightseeingMarkerPath = "bgcommon/world/common/vfx_for_live/eff/b0810_tnsk_y.avfx";
    private const string UnavailableMarkerPath = "bgcommon/world/common/vfx_for_live/eff/b0132_rass_y.avfx";
    private const string BlockedMarkerPath = "bgcommon/world/common/vfx_for_live/eff/b0131_rasp_y.avfx";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        clientState.TerritoryChanged += OnTerritoryChange;

        SpawnVfxForCurrentZone();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        clientState.TerritoryChanged -= OnTerritoryChange;

        return Task.CompletedTask;
    }

    private void OnTerritoryChange(uint territory)
    {
        if (!pluginConfig.ShowArrVistas)
        {
            return;
        }

        try
        {
            vfxService.QueueRemoveAll();
            SpawnVfxForZone(territory);
        }
        catch (Exception e)
        {
            pluginLog.Error(e, "Exception in territory change");
        }
    }

    internal void SpawnVfxForCurrentZone()
    {
        SpawnVfxForZone(clientState.TerritoryType);
    }

    internal void RemoveAllVfx()
    {
        vfxService.QueueRemoveAll();
    }

    private void SpawnVfxForZone(uint territory)
    {
        var row = 0;
        foreach (var adventure in dataManager.GetExcelSheet<Adventure>())
        {
            bool blocked = false;
            if (row >= 20 && row < 80 && !MainWindow.ARRVistasExpanded)
                blocked = true;
            if (row >= 80)
            {
                break;
            }

            row += 1;

            if (adventure.Level.ValueNullable?.Territory.RowId != territory)
            {
                continue;
            }

            if (unlockState.IsAdventureComplete(adventure))
            {
                continue;
            }

            var loc = adventure.Level.Value;
            var pos = new Vector3(loc.X, loc.Z, loc.Y + 0.5f);

            var path = SightseeingMarkerPath;
            if (!adventure.Available(weatherLuminaService))
                path = UnavailableMarkerPath;
            if (blocked)
                path = BlockedMarkerPath;
            vfxService.QueueSpawn(row, path, pos, Quaternion.Zero);
        }
    }
}
