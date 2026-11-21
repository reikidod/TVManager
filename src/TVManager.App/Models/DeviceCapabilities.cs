namespace TVManager.Models;

public sealed record DeviceCapabilities(
    bool CanReadInfo,
    bool CanManageApps,
    bool CanChangeDisplaySound,
    bool CanReadNetworkInputs,
    bool CanUseProfilesAndRollback)
{
    public static DeviceCapabilities None => new(false, false, false, false, false);

    public IReadOnlyList<string> UnsupportedReasons
    {
        get
        {
            var reasons = new List<string>();
            if (!CanReadInfo) reasons.Add("чтение сведений");
            if (!CanManageApps) reasons.Add("управление приложениями");
            if (!CanChangeDisplaySound) reasons.Add("изменение экрана и звука");
            if (!CanReadNetworkInputs) reasons.Add("сеть и входы");
            if (!CanUseProfilesAndRollback) reasons.Add("профили и откат");
            return reasons;
        }
    }
}
