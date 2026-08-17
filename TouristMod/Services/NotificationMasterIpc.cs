using NotificationMasterAPI;
using Dalamud.Plugin;
using TouristMod.Config;
using Microsoft.Extensions.Logging;

namespace TouristMod.Services;

public sealed class NotificationMasterIpc(
    IDalamudPluginInterface pluginInterface,
    PluginConfig pluginConfig,
    ILogger<NotificationMasterIpc> logger)
{
    private readonly NotificationMasterApi _api = new(pluginInterface);
    public bool Enabled => pluginConfig.Notify && _api.IsIPCReady();

    public void DisplayTray(string title, string message)
    {
        if (!Enabled)
        {
            logger.LogInformation("Tray notification attempted, but notifications were disabled: {Title} / {Message}", title, message);
            return;
        }
        _api.DisplayTrayNotification(title, message);
    }
}
