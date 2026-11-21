namespace TVManager.Models;

public sealed record AppInfo(string PackageId, string Label, string Version, bool IsSystem)
{
    public string KindLabel => IsSystem ? "Системное" : "Пользовательское";
}
