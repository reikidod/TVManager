using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using TVManager.Models;
using TVManager.Services;

namespace TVManager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DiscoveryService _discovery = new();
    private CancellationTokenSource? _scanCts;
    private TvDevice? _selectedDevice;
    private string _statusText = "Готово к поиску";
    private string _logText = "Ожидается запуск автоматического обнаружения.";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<TvDevice> Devices { get; } = [];

    public TvDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string DeviceCountText => $"{Devices.Count} найдено";

    public string LogText
    {
        get => _logText;
        private set { _logText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCts is not null) return;

        _scanCts = new CancellationTokenSource();
        ScanButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        StatusText = "Идёт поиск устройств…";
        LogText = "Проверяем ADB, SDB, SSDP/UPnP, диагностические TCP-порты и Windows USB/PnP.";
        Devices.Clear();
        OnPropertyChanged(nameof(DeviceCountText));
        SelectedDevice = null;

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

    protected override void OnClosing(CancelEventArgs e)
    {
        _scanCts?.Cancel();
        base.OnClosing(e);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
