using Dalamud.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace TouristMod.Config;

[SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
public class PluginConfig : IPluginConfiguration
{
    public bool OnlyShowCurrentZone { get; set; } = false;
    public bool ShowArrVistas { get; set; } = true;
    public bool ShowFinished { get; set; } = true;
    public bool ShowTimeLeft { get; set; } = true;
    public bool ShowTimeUntilAvailable { get; set; } = true;
    public bool ShowUnavailable { get; set; } = true;
    public bool ShowBlocked { get; set; }
    public bool Notify { get; set; } = true;
    public bool DefaultOpen { get; set; }
    public SortMode SortMode { get; set; } = SortMode.Number;
    public int Version { get; set; } = 1;
}

[Serializable]
public enum SortMode
{
    Number,
    Zone,
}
