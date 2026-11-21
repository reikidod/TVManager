using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using TVManager.Models;
using TVManager.Services;
using TVManager.Services.Connections;

namespace TVManager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DiscoveryService _discovery = new();
    private readonly ConnectionManager _connections = new();
    private readonly ProfileService _profiles = new();
    private readonly Dictionary<string, SettingChange> _lastChanges = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _connectionCts;
    private TvDevice? _selectedDevice;
    private SettingInfo? _selectedSetting;
    private AppInfo? _selectedApp;
    private DeviceProfile? _selectedProfile;
    private DeviceInfo? _deviceInfo;
    private DeviceProfile? _profileUndo;

    private string _statusText = "Готово к поиску";
    private string _logText = "Ожидается запуск автоматического обнаружения.";
    private string _connectionText = "Нет активного подключения";
    private string _capabilitiesText = "—";
    private string _deviceInfoText = "—";
    private string _connectionNotesText = string.Empty;
    private string _profileName = string.Empty;
    private string _actionLogText = "Действия управления появятся здесь.";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadProfiles();
    }

    public ObservableCollection<TvDevice> Devices { get; } = [];
    public ObservableCollection<SettingInfo> Settings { get; } = [];
    public ObservableCollection<AppInfo> Apps { get; } = [];
    public ObservableCollection<DeviceProfile> Profiles { get; } = [];

    public TvDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConnect));
        }
    }

    public SettingInfo? SelectedSetting
    {
        get => _selectedSetting;
        set { _selectedSetting = value; OnPropertyChanged(); }
    }

    public AppInfo? SelectedApp
    {
        get => _selectedApp;
        set { _selectedApp = value; OnPropertyChanged(); }
    }

    public DeviceProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string LogText
    {
        get => _logText;
        private set { _logText = value; OnPropertyChanged(); }
    }

    public string ConnectionText
    {
        get => _connectionText;
        private set { _connectionText = value; OnPropertyChanged(); }
    }

    public string CapabilitiesText
    {
        get => _capabilitiesText;
        private set { _capabilitiesText = value; OnPropertyChanged(); }
    }

    public string DeviceInfoText
    {
        get => _deviceInfoText;
        private set { _deviceInfoText = value; OnPropertyChanged(); }
    }

    public string ConnectionNotesText
    {
        get => _connectionNotesText;
        private set { _connectionNotesText = value; OnPropertyChanged(); }
    }

    public string ProfileName
    {
        get => _profileName;
        set { _profileName = value; OnPropertyChanged(); }
    }

    public string ActionLogText
    {
        get => _actionLogText;
        private set { _actionLogText = value; OnPropertyChanged(); }
    }

    public string DeviceCountText => $"{Devices.Count} найдено";

    public bool CanConnect => SelectedDevice is not null && !_connections.IsConnected;
    public bool CanDisconnect => _connections.IsConnected;
    public bool CanRefresh => _connections.IsConnected;
    public bool CanEditSettings => _connections.Current?.Capabilities.CanChangeDisplaySound == true;
    public bool CanManageApps => _connections.Current?.Capabilities.CanManageApps == true;
    public bool CanUseProfiles => _connections.Current?.Capabilities.CanUseProfilesAndRollback == true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCts is not null) return;
        if (_connections.IsConnected)
            await _connections.DisconnectAsync(CancellationToken.None);

        _scanCts = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        ScanButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        StatusText = "Идёт поиск устройств…";
        LogText = "Проверяем ADB, SDB, SSDP/UPnP, диагностические TCP-порты и Windows USB/PnP.";
        Devices.Clear();
        OnPropertyChanged(nameof(DeviceCountText));
        SelectedDevice = null;
        ClearConnectionData();

        try
        {
            var report = await _discovery.ScanAsync(_scanCts.Token);
            foreach (var device in report.Devices)
                Devices.Add(device);

            OnPropertyChanged(nameof(DeviceCountText));
            StatusText = $"Поиск завершён · {report.Devices.Count} устройств · {report.Duration.TotalSeconds:F1} сек.";
            LogText = report.Notes.Count == 0
                ? "Поиск завершён. Дополнительных замечаний нет. Проверьте степень достоверности каждого найденного устройства."
                : string.Join("  •  ", report.Notes);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Поиск остановлен пользователем";
            LogText = "Сканирование было отменено. Уже найденные в этом запуске результаты не сохранялись.";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка поиска";
            LogText = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка обнаружения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            ScanButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            ScanProgress.IsIndeterminate = false;
            ScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is null) return;
        _connectionCts = new CancellationTokenSource();
        StatusText = $"Подключение к {SelectedDevice.Name}…";

        try
        {
            await _connections.ConnectAsync(SelectedDevice, _connectionCts.Token);
            await RefreshDeviceAsync(_connectionCts.Token);
            StatusText = $"Подключено: {SelectedDevice.Name}";
            LogText = $"Подключение к {SelectedDevice.Name} установлено. Доступные операции определены по возможностям платформы.";
            ActionLogText = "Подключение установлено, сведения и параметры обновлены.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Подключение отменено";
            ActionLogText = "Подключение отменено пользователем.";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка подключения";
            ActionLogText = ex.Message;
            MessageBox.Show(this, ex.Message, "Не удалось подключиться", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _connectionCts.Dispose();
            _connectionCts = null;
            UpdateConnectionUi();
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _connections.DisconnectAsync(CancellationToken.None);
            ClearConnectionData();
            StatusText = "Подключение закрыто";
            ActionLogText = "Подключение закрыто.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка отключения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateConnectionUi();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_connections.IsConnected) return;
        try
        {
            await RefreshDeviceAsync(CancellationToken.None);
            ActionLogText = "Сведения, параметры и приложения обновлены.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshDeviceAsync(CancellationToken token)
    {
        var controller = _connections.RequireController();
        var connection = _connections.Current!;

        _deviceInfo = await controller.GetInfoAsync(connection, token);
        var settings = await controller.GetSettingsAsync(connection, token);
        var apps = await controller.GetAppsAsync(connection, token);

        Settings.Clear();
        foreach (var setting in settings) Settings.Add(setting);
        Apps.Clear();
        foreach (var app in apps) Apps.Add(app);
        UpdateConnectionUi();
    }

    private async void ApplySetting_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSetting is null || !CanEditSettings) return;
        try
        {
            var controller = _connections.RequireController();
            var connection = _connections.Current!;
            var value = SelectedSetting.Value;
            var preview = await controller.ApplySettingAsync(connection, SelectedSetting.Key, value, true, CancellationToken.None);
            if (!preview.Success)
            {
                ActionLogText = preview.Message;
                return;
            }

            var text = preview.Preview is null ? string.Empty : string.Join(Environment.NewLine, preview.Preview);
            var confirm = MessageBox.Show(this,
                $"{SelectedSetting.Label}: {value}\n\n{text}\n\nПрименить изменение?",
                "Подтверждение изменения", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var result = await controller.ApplySettingAsync(connection, SelectedSetting.Key, value, false, CancellationToken.None);
            ActionLogText = result.Message;
            if (result.Success && result.Undo is not null)
                _lastChanges[result.Undo.Key] = result.Undo;
            await RefreshDeviceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ActionLogText = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка изменения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UndoSetting_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSetting is null || !CanEditSettings) return;
        if (!_lastChanges.TryGetValue(SelectedSetting.Key, out var change))
        {
            ActionLogText = "Для этого параметра нет сохранённого предыдущего значения.";
            return;
        }

        try
        {
            var controller = _connections.RequireController();
            var result = await controller.UndoSettingAsync(_connections.Current!, change, CancellationToken.None);
            ActionLogText = result.Message;
            if (result.Success) _lastChanges.Remove(change.Key);
            await RefreshDeviceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ActionLogText = ex.Message;
        }
    }

    private async void UninstallApp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null || !CanManageApps) return;
        try
        {
            var controller = _connections.RequireController();
            var connection = _connections.Current!;
            var preview = await controller.UninstallAppAsync(connection, SelectedApp, true, CancellationToken.None);
            var text = preview.Preview is null ? string.Empty : string.Join(Environment.NewLine, preview.Preview);
            var confirm = MessageBox.Show(this,
                $"Удалить {SelectedApp.PackageId}?\n\n{text}",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var result = await controller.UninstallAppAsync(connection, SelectedApp, false, CancellationToken.None);
            ActionLogText = result.Message;
            await RefreshDeviceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ActionLogText = ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!CanUseProfiles || _connections.Current is null) return;
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            MessageBox.Show(this, "Введите имя профиля.", "Профиль", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var writable = Settings.Where(x => x.IsWritable);
        _profiles.Save(_profiles.Snapshot(ProfileName.Trim(), _connections.Current, writable));
        LoadProfiles();
        ActionLogText = $"Профиль «{ProfileName.Trim()}» сохранён.";
    }

    private async void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is null || !CanUseProfiles || _connections.Current is null) return;
        var confirm = MessageBox.Show(this,
            $"Применить профиль «{SelectedProfile.Name}» к {_connections.Current.Device.Name}?\n" +
            "Текущие значения будут сохранены для отката.",
            "Подтверждение профиля", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var controller = _connections.RequireController();
            var connection = _connections.Current;
            _profileUndo = _profiles.Snapshot($"{SelectedProfile.Name} · до применения", connection,
                Settings.Where(x => x.IsWritable));

            var errors = new List<string>();
            foreach (var pair in SelectedProfile.Settings)
            {
                var result = await controller.ApplySettingAsync(connection, pair.Key, pair.Value, false, CancellationToken.None);
                if (!result.Success) errors.Add($"{pair.Key}: {result.Message}");
            }

            ActionLogText = errors.Count == 0
                ? $"Профиль «{SelectedProfile.Name}» применён."
                : $"Профиль применён частично: {string.Join("; ", errors)}";
            await RefreshDeviceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ActionLogText = ex.Message;
        }
    }

    private async void UndoProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profileUndo is null || !CanUseProfiles || _connections.Current is null)
        {
            ActionLogText = "Нет сохранённой точки отката профиля.";
            return;
        }

        try
        {
            var controller = _connections.RequireController();
            var connection = _connections.Current;
            foreach (var pair in _profileUndo.Settings)
                await controller.ApplySettingAsync(connection, pair.Key, pair.Value, false, CancellationToken.None);

            ActionLogText = $"Откат к состоянию «{_profileUndo.Name}» выполнен.";
            _profileUndo = null;
            await RefreshDeviceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ActionLogText = ex.Message;
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is null) return;
        var confirm = MessageBox.Show(this, $"Удалить профиль «{SelectedProfile.Name}»?",
            "Удаление профиля", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _profiles.Delete(SelectedProfile.Name);
        LoadProfiles();
        ActionLogText = $"Профиль «{SelectedProfile.Name}» удалён.";
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _profiles.LoadAll()) Profiles.Add(profile);
    }

    private void ClearConnectionData()
    {
        _deviceInfo = null;
        _profileUndo = null;
        _lastChanges.Clear();
        Settings.Clear();
        Apps.Clear();
        SelectedSetting = null;
        SelectedApp = null;
        ConnectionText = "Нет активного подключения";
        CapabilitiesText = "—";
        DeviceInfoText = "—";
        ConnectionNotesText = string.Empty;
    }

    private void UpdateConnectionUi()
    {
        var connection = _connections.Current;
        if (connection is null)
        {
            ConnectionText = "Нет активного подключения";
            CapabilitiesText = "—";
            DeviceInfoText = "—";
            ConnectionNotesText = string.Empty;
        }
        else
        {
            ConnectionText = $"{connection.Device.Name} · {connection.StateLabel}\n{connection.StatusText}";
            var capabilities = connection.Capabilities;
            var available = new List<string>();
            if (capabilities.CanReadInfo) available.Add("чтение сведений");
            if (capabilities.CanManageApps) available.Add("приложения");
            if (capabilities.CanChangeDisplaySound) available.Add("экран и звук");
            if (capabilities.CanReadNetworkInputs) available.Add("сеть и входы");
            if (capabilities.CanUseProfilesAndRollback) available.Add("профили и откат");
            CapabilitiesText = $"Доступно: {(available.Count == 0 ? "нет" : string.Join(", ", available))}.\n" +
                               $"Недоступно: {(capabilities.UnsupportedReasons.Count == 0 ? "нет" : string.Join(", ", capabilities.UnsupportedReasons))}.";

            var info = _deviceInfo;
            DeviceInfoText = info is null
                ? "—"
                : string.Join(Environment.NewLine,
                    $"Производитель: {info.Manufacturer}",
                    $"Модель: {info.Model}",
                    $"Прошивка: {info.Firmware}",
                    $"Платформа: {info.PlatformVersion}",
                    $"Серийный номер: {info.Serial}",
                    $"Разрешение: {info.Resolution}");
            ConnectionNotesText = info is null ? string.Empty : string.Join(Environment.NewLine, info.Notes);
        }

        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanManageApps));
        OnPropertyChanged(nameof(CanUseProfiles));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _scanCts?.Cancel();
        _connectionCts?.Cancel();
        base.OnClosing(e);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
