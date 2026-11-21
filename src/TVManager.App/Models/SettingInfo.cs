namespace TVManager.Models;

public sealed class SettingInfo
{
    public SettingInfo(string key, string label, string value, string description, bool isWritable)
    {
        Key = key;
        Label = label;
        Value = value;
        Description = description;
        IsWritable = isWritable;
    }

    public string Key { get; }
    public string Label { get; }
    public string Value { get; set; }
    public string Description { get; }
    public bool IsWritable { get; }
}
