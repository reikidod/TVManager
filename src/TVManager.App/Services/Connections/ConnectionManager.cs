using TVManager.Models;

namespace TVManager.Services.Connections;

internal sealed class ConnectionManager
{
    private IDeviceController? _controller;

    public TvConnection? Current { get; private set; }
    public IDeviceController? Controller => _controller;
    public bool IsConnected => Current?.State == ConnectionState.Connected;

    public event EventHandler? Changed;

    public async Task<TvConnection> ConnectAsync(TvDevice device, CancellationToken token)
    {
        var controller = CreateController(device.Platform)
            ?? throw new InvalidOperationException($"Платформа {device.PlatformLabel} пока не поддерживает подключение.");

        var connection = await controller.ConnectAsync(device, token);
        Current = connection;
        _controller = controller;
        Changed?.Invoke(this, EventArgs.Empty);
        return connection;
    }

    public async Task DisconnectAsync(CancellationToken token)
    {
        if (Current is not null && _controller is not null)
            await _controller.DisconnectAsync(Current, token);

        if (Current is not null)
        {
            Current.State = ConnectionState.Disconnected;
            Current.StatusText = "Подключение закрыто";
        }
        Current = null;
        _controller = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IDeviceController RequireController() =>
        _controller ?? throw new InvalidOperationException("Нет активного подключения к телевизору.");

    private static IDeviceController? CreateController(DevicePlatform platform) => platform switch
    {
        DevicePlatform.AndroidTv or DevicePlatform.FireOs or DevicePlatform.AndroidFamily => new AdbController(),
        DevicePlatform.SamsungTizen => new SdbController(),
        DevicePlatform.LgWebOs => new WebOsController(),
        _ => null
    };
}
