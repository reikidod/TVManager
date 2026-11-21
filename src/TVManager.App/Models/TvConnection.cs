using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TVManager.Models;

public sealed class TvConnection : INotifyPropertyChanged
{
    private ConnectionState _state;
    private string _statusText = string.Empty;

    public TvConnection(TvDevice device, DeviceCapabilities capabilities, string endpoint)
    {
        Device = device;
        Capabilities = capabilities;
        Endpoint = endpoint;
    }

    public TvDevice Device { get; }
    public DevicePlatform Platform => Device.Platform;
    public DeviceCapabilities Capabilities { get; }
    public string Endpoint { get; }
    public DateTimeOffset? ConnectedAt { get; internal set; }

    public ConnectionState State
    {
        get => _state;
        internal set { _state = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        internal set { _statusText = value; OnPropertyChanged(); }
    }

    public string StateLabel => State switch
    {
        ConnectionState.Connecting => "Подключение…",
        ConnectionState.Connected => "Подключено",
        ConnectionState.Failed => "Ошибка подключения",
        _ => "Не подключено"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
