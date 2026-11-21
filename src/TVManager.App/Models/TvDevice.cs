using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TVManager.Models;

public sealed class TvDevice : INotifyPropertyChanged
{
    private string _status;
    private string _details;

    public TvDevice(string id, string name, DevicePlatform platform, ConnectionKind connection,
        DetectionConfidence confidence, string address, string evidence, string status, string details)
    {
        Id = id;
        Name = name;
        Platform = platform;
        Connection = connection;
        Confidence = confidence;
        Address = address;
        Evidence = evidence;
        _status = status;
        _details = details;
    }

    public string Id { get; }
    public string Name { get; }
    public DevicePlatform Platform { get; }
    public string PlatformLabel => Platform switch
    {
        DevicePlatform.AndroidTv => "Android TV / Google TV",
        DevicePlatform.FireOs => "Amazon Fire OS",
        DevicePlatform.SamsungTizen => "Samsung Tizen",
        DevicePlatform.LgWebOs => "LG webOS",
        DevicePlatform.AndroidFamily => "Android / Fire OS (не уточнено)",
        _ => "Не определена"
    };
    public ConnectionKind Connection { get; }
    public string ConnectionLabel => Connection == ConnectionKind.Usb ? "USB" : "Сеть";
    public DetectionConfidence Confidence { get; }
    public string ConfidenceLabel => Confidence switch
    {
        DetectionConfidence.Confirmed => "Подтверждено",
        DetectionConfidence.Probable => "Вероятно",
        _ => "Предварительно"
    };
    public string Address { get; }
    public string Evidence { get; }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string Details
    {
        get => _details;
        set { _details = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
