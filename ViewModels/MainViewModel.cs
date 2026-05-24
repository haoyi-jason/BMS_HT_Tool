using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using BmsHostUi.Models;
using BmsHostUi.Services;

namespace BmsHostUi.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        public sealed class SeriesSnapshot
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public bool IsLeftAxis { get; set; }
            public double[] Values { get; set; }
        }

        private sealed class LiveMapItem
        {
            public string Name { get; set; }
            public string Unit { get; set; }
            public string Type { get; set; }
        }

        private static readonly LiveMapItem[] LiveMap =
        {
            new LiveMapItem { Name = "BAT_VOLT", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CURRENT", Unit = "mA", Type = "I16" },
            new LiveMapItem { Name = "SOC", Unit = "%", Type = "U8" },
            new LiveMapItem { Name = "CT_MAX", Unit = "C", Type = "I8" },
            new LiveMapItem { Name = "CT_MIN", Unit = "C", Type = "I8" },
            new LiveMapItem { Name = "CV_MAX", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV_MIN", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "OCC_THRESHOLD", Unit = "mA", Type = "I16" },
            new LiveMapItem { Name = "OCD_THRESHOLD", Unit = "mA", Type = "I16" },
            new LiveMapItem { Name = "BATTERY_STATE", Unit = "", Type = "U8" },
            new LiveMapItem { Name = "SOH", Unit = "%", Type = "U8" },
            new LiveMapItem { Name = "BALANCE_TARGET", Unit = "", Type = "U8" },
            new LiveMapItem { Name = "BATTERY_FLAGS", Unit = "", Type = "U8" },
            new LiveMapItem { Name = "CYCLE_COUNT", Unit = "", Type = "U16" },
            new LiveMapItem { Name = "FW_VER", Unit = "", Type = "U16" },
            new LiveMapItem { Name = "CV1", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV2", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV3", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV4", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV5", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV6", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV7", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "CV8", Unit = "mV", Type = "U16" },
            new LiveMapItem { Name = "NTC1", Unit = "C", Type = "I8" },
            new LiveMapItem { Name = "NTC2", Unit = "C", Type = "I8" },
            new LiveMapItem { Name = "NTC3", Unit = "C", Type = "I8" },
            new LiveMapItem { Name = "NTC4", Unit = "C", Type = "I8" },
            new LiveMapItem { Name = "CHARGE_VOLT", Unit = "mV", Type = "U16" },
        };

        private static readonly HashSet<string> AutoManagedLiveSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BATTERY_STATE",
            "BATTERY_FLAGS",
            "FW_VER",
        };

        private readonly IModbusService _modbusService;
        private readonly CsvLoggerService _csvLoggerService;
        private readonly GitHubUpdateService _updateService;
        private readonly AppSettingsService _appSettingsService;
        private readonly object _seriesLock = new object();
        private readonly Dictionary<int, Queue<double>> _historyByIndex = new Dictionary<int, Queue<double>>();
        private readonly HashSet<int> _leftSelected = new HashSet<int>();
        private readonly HashSet<int> _rightSelected = new HashSet<int>();

        private CancellationTokenSource _pollingCts;
        private bool _isRunning;
        private bool _isBusy;
        private bool _isY1AutoRange;
        private bool _isY2AutoRange;
        private double _y1Min;
        private double _y1Max;
        private double _y2Min;
        private double _y2Max;
        private string _dataFlashKeyText;
        private string _csvPath;
        private string _dfCsvPath;
        private string _dfCsvStatusText;
        private string _dfFilterText;
        private bool _isHexDisplay;
        private string _githubOwner;
        private string _githubRepo;
        private ParameterRow _selectedParameter;
        private string _firmwareVersion = "N/A";
        private string _connectButtonText = "Connect";
        private int _batteryStateRaw;
        private int _batteryFlagsRaw;

        public MainViewModel(IModbusService modbusService, CsvLoggerService csvLoggerService)
        {
            _modbusService = modbusService;
            _csvLoggerService = csvLoggerService;
            _updateService = new GitHubUpdateService();
            _appSettingsService = new AppSettingsService();
            _modbusService.Log += (_, msg) => SetStatus(msg);

            Ports = new ObservableCollection<string>(SerialPort.GetPortNames().OrderBy(x => x));
            SelectedPort = Ports.FirstOrDefault();
            BaudRate = 9600;
            SlaveId = 1;
            PollPeriodSeconds = 1;
            LatestCount = 300;
            CsvPath = System.IO.Path.Combine(Environment.CurrentDirectory, "logs", "bms_live.csv");
            StatusText = L("StatusIdle", "Idle");
            IsY1AutoRange = true;
            IsY2AutoRange = true;
            Y1Min = 0;
            Y1Max = 35000;
            Y2Min = -5000;
            Y2Max = 5000;
            DataFlashKeyText = string.Empty;
            DfCsvPath = ResolveTemplateCsvPath();
            DfCsvStatusText = string.Empty;
            IsHexDisplay = false;
            var appSettings = _appSettingsService.Load();
            GitHubOwner = appSettings.GitHubOwner ?? "haoyi-jason";
            GitHubRepo = appSettings.GitHubRepo ?? "BMS_HY_Tool";
            _connectButtonText = "Connect";
            _firmwareVersion = "N/A";

            LeftRegisters = new ObservableCollection<RegisterSelectionItem>(
                LiveMap.Select((x, i) => new { Item = x, Index = i })
                .Where(x => !AutoManagedLiveSymbols.Contains(x.Item.Name))
                .Select(x => new RegisterSelectionItem
                {
                    Index = x.Index,
                    Name = x.Item.Name
                }));
            RightRegisters = new ObservableCollection<RegisterSelectionItem>(
                LiveMap.Select((x, i) => new { Item = x, Index = i })
                .Where(x => !AutoManagedLiveSymbols.Contains(x.Item.Name))
                .Select(x => new RegisterSelectionItem
                {
                    Index = x.Index,
                    Name = x.Item.Name
                }));
            LiveRows = new ObservableCollection<LiveDataRow>(
                LiveMap.Select((item, index) => new LiveDataRow
                {
                    Name = item.Name,
                    AddressHex = index.ToString(CultureInfo.InvariantCulture),
                    ValueType = item.Type,
                    RawValue = 0,
                    ValueText = string.Empty,
                    Unit = item.Unit,
                    Timestamp = DateTime.MinValue
                }));
            ParameterRows = new ObservableCollection<ParameterRow>(LoadDataFlashDefinitions());
            ParameterRowsView = CollectionViewSource.GetDefaultView(ParameterRows);
            ParameterRowsView.Filter = FilterParameterRow;

            ConnectToggleCommand = new RelayCommand(() => RunCommand(_modbusService.IsConnected ? DisconnectAsync : ConnectAsync), () => !_isBusy);
            StartCommand = new RelayCommand(() => RunCommand(StartAsync), () => _modbusService.IsConnected && !_isRunning && !_isBusy);
            StopCommand = new RelayCommand(() => RunCommand(StopAsync), () => _isRunning);
            ReadParamsCommand = new RelayCommand(() => RunCommand(ReadAllParametersAsync), () => _modbusService.IsConnected && !_isRunning && !_isBusy);
            WriteParamsCommand = new RelayCommand(() => RunCommand(WriteAllParametersAsync), () => _modbusService.IsConnected && !_isRunning && !_isBusy);
            ReadSelectedParamCommand = new RelayCommand(() => RunCommand(ReadSelectedParameterAsync), () => _modbusService.IsConnected && !_isRunning && !_isBusy && SelectedParameter != null);
            WriteSelectedParamCommand = new RelayCommand(() => RunCommand(WriteSelectedParameterAsync), () => _modbusService.IsConnected && !_isRunning && !_isBusy && SelectedParameter != null);
            LoadDfCsvCommand = new RelayCommand(() => RunCommand(LoadDfFromCsvAsync), () => !_isRunning && !_isBusy);
            SaveDfCsvCommand = new RelayCommand(() => RunCommand(SaveDfToCsvAsync), () => !_isRunning && !_isBusy);
            LoadLogCommand = new RelayCommand(() => RunCommand(LoadLogForPlotAsync), () => !_isRunning && !_isBusy);
            BrowseCsvPathCommand = new RelayCommand(BrowseCsvPath, () => !_isRunning && !_isBusy);
            BrowseDfCsvPathCommand = new RelayCommand(BrowseDfCsvPath, () => !_isRunning && !_isBusy);
            CheckUpdateCommand = new RelayCommand(() => RunCommand(CheckForUpdateAsync), () => !_isRunning && !_isBusy);
            WriteKeyCommand = new RelayCommand(() => RunCommand(WriteDataFlashKeyAsync), () => _modbusService.IsConnected && !_isRunning && !_isBusy);
            RefreshPortsCommand = new RelayCommand(RefreshPorts);
        }

        public ObservableCollection<string> Ports { get; }
        public ObservableCollection<RegisterSelectionItem> LeftRegisters { get; }
        public ObservableCollection<RegisterSelectionItem> RightRegisters { get; }
        public ObservableCollection<LiveDataRow> LiveRows { get; }
        public ObservableCollection<ParameterRow> ParameterRows { get; }
        public ICollectionView ParameterRowsView { get; }

        public ICommand ConnectToggleCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ReadParamsCommand { get; }
        public ICommand WriteParamsCommand { get; }
        public ICommand ReadSelectedParamCommand { get; }
        public ICommand WriteSelectedParamCommand { get; }
        public ICommand LoadDfCsvCommand { get; }
        public ICommand SaveDfCsvCommand { get; }
        public ICommand LoadLogCommand { get; }
        public ICommand BrowseCsvPathCommand { get; }
        public ICommand BrowseDfCsvPathCommand { get; }
        public ICommand CheckUpdateCommand { get; }
        public ICommand WriteKeyCommand { get; }
        public ICommand RefreshPortsCommand { get; }

        public string SelectedPort { get; set; }
        public int BaudRate { get; set; }
        public int SlaveId { get; set; }
        public int PollPeriodSeconds { get; set; }
        public bool AccumulateMode { get; set; }
        public int LatestCount { get; set; }
        public string CsvPath
        {
            get { return _csvPath; }
            set
            {
                if (_csvPath == value)
                {
                    return;
                }

                _csvPath = value;
                OnPropertyChanged(nameof(CsvPath));
            }
        }

        public string DfCsvPath
        {
            get { return _dfCsvPath; }
            set
            {
                if (_dfCsvPath == value)
                {
                    return;
                }

                _dfCsvPath = value;
                OnPropertyChanged(nameof(DfCsvPath));
            }
        }

        public string DfCsvStatusText
        {
            get { return _dfCsvStatusText; }
            set
            {
                if (_dfCsvStatusText == value)
                {
                    return;
                }

                _dfCsvStatusText = value;
                OnPropertyChanged(nameof(DfCsvStatusText));
            }
        }

        public string DfFilterText
        {
            get { return _dfFilterText; }
            set
            {
                if (_dfFilterText == value)
                {
                    return;
                }

                _dfFilterText = value;
                OnPropertyChanged(nameof(DfFilterText));
                RefreshParameterFilter();
            }
        }

        public bool IsHexDisplay
        {
            get { return _isHexDisplay; }
            set
            {
                if (_isHexDisplay == value)
                {
                    return;
                }

                _isHexDisplay = value;
                OnPropertyChanged(nameof(IsHexDisplay));
                RefreshLiveRowsValueText();
                RefreshParameterValueText();
            }
        }

        public string GitHubOwner
        {
            get { return _githubOwner; }
            set
            {
                if (_githubOwner == value)
                {
                    return;
                }

                _githubOwner = value;
                OnPropertyChanged(nameof(GitHubOwner));
                SaveAppSettings();
            }
        }

        public string GitHubRepo
        {
            get { return _githubRepo; }
            set
            {
                if (_githubRepo == value)
                {
                    return;
                }

                _githubRepo = value;
                OnPropertyChanged(nameof(GitHubRepo));
                SaveAppSettings();
            }
        }

        public ParameterRow SelectedParameter
        {
            get { return _selectedParameter; }
            set
            {
                if (_selectedParameter == value)
                {
                    return;
                }

                _selectedParameter = value;
                OnPropertyChanged(nameof(SelectedParameter));
                RaiseCommandState();
            }
        }

        public string DataFlashKeyText
        {
            get { return _dataFlashKeyText; }
            set
            {
                if (_dataFlashKeyText == value)
                {
                    return;
                }

                _dataFlashKeyText = value;
                OnPropertyChanged(nameof(DataFlashKeyText));
            }
        }

        public bool IsY1AutoRange
        {
            get { return _isY1AutoRange; }
            set
            {
                if (_isY1AutoRange == value)
                {
                    return;
                }

                _isY1AutoRange = value;
                OnPropertyChanged(nameof(IsY1AutoRange));
            }
        }

        public bool IsY2AutoRange
        {
            get { return _isY2AutoRange; }
            set
            {
                if (_isY2AutoRange == value)
                {
                    return;
                }

                _isY2AutoRange = value;
                OnPropertyChanged(nameof(IsY2AutoRange));
            }
        }

        public double Y1Min
        {
            get { return _y1Min; }
            set
            {
                if (Math.Abs(_y1Min - value) < 1e-9)
                {
                    return;
                }

                _y1Min = value;
                OnPropertyChanged(nameof(Y1Min));
            }
        }

        public double Y1Max
        {
            get { return _y1Max; }
            set
            {
                if (Math.Abs(_y1Max - value) < 1e-9)
                {
                    return;
                }

                _y1Max = value;
                OnPropertyChanged(nameof(Y1Max));
            }
        }

        public double Y2Min
        {
            get { return _y2Min; }
            set
            {
                if (Math.Abs(_y2Min - value) < 1e-9)
                {
                    return;
                }

                _y2Min = value;
                OnPropertyChanged(nameof(Y2Min));
            }
        }

        public double Y2Max
        {
            get { return _y2Max; }
            set
            {
                if (Math.Abs(_y2Max - value) < 1e-9)
                {
                    return;
                }

                _y2Max = value;
                OnPropertyChanged(nameof(Y2Max));
            }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string ConnectButtonText
        {
            get { return _connectButtonText; }
            private set
            {
                if (_connectButtonText == value)
                {
                    return;
                }

                _connectButtonText = value;
                OnPropertyChanged(nameof(ConnectButtonText));
            }
        }

        public string FirmwareVersion
        {
            get { return _firmwareVersion; }
            private set
            {
                if (_firmwareVersion == value)
                {
                    return;
                }

                _firmwareVersion = value;
                OnPropertyChanged(nameof(FirmwareVersion));
            }
        }

        public int BatteryStateRaw
        {
            get { return _batteryStateRaw; }
            private set
            {
                if (_batteryStateRaw == value)
                {
                    return;
                }

                _batteryStateRaw = value;
                OnPropertyChanged(nameof(BatteryStateRaw));
            }
        }

        public int BatteryFlagsRaw
        {
            get { return _batteryFlagsRaw; }
            private set
            {
                if (_batteryFlagsRaw == value)
                {
                    return;
                }

                _batteryFlagsRaw = value;
                OnPropertyChanged(nameof(BatteryFlagsRaw));
            }
        }

        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedPort))
            {
                SetStatus(L("MsgSelectCom", "Please select COM port."));
                return;
            }

            await _modbusService.ConnectAsync(SelectedPort, BaudRate, (byte)SlaveId, CancellationToken.None);
            UpdateConnectionState();
            RaiseCommandState();
        }

        private async Task StartAsync()
        {
            if (_isRunning)
            {
                return;
            }

            bool overwrite = true;
            if (System.IO.File.Exists(CsvPath))
            {
                var result = MessageBox.Show(
                    L("MsgCsvOverwriteConfirm", "CSV already exists. Overwrite it?"),
                    L("TitleCsvConfirm", "CSV Confirm"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    overwrite = true;
                }
                else
                {
                    SetStatus(L("MsgStartCanceled", "Start canceled by user."));
                    return;
                }
            }

            _csvLoggerService.EnsureWritableFile(CsvPath, overwrite);
            _pollingCts = new CancellationTokenSource();
            _isRunning = true;
            RaiseCommandState();
            SetStatus(L("MsgPollingStarted", "Polling started."));

            try
            {
                while (!_pollingCts.IsCancellationRequested)
                {
                    try
                    {
                        await PollOnceAsync(_pollingCts.Token);
                    }
                    catch (Exception ex)
                    {
                        SetStatus(LF("MsgPollingWarningFmt", "Polling warning: {0}", ex.Message));
                    }

                    await Task.Delay(Math.Max(1, PollPeriodSeconds) * 1000, _pollingCts.Token);
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                _isRunning = false;
                RaiseCommandState();
                SetStatus(L("MsgPollingStopped", "Polling stopped."));
            }
        }

        private async Task DisconnectAsync()
        {
            if (_isRunning)
            {
                _pollingCts?.Cancel();
                _isRunning = false;
            }

            await _modbusService.DisconnectAsync();
            FirmwareVersion = "N/A";
            BatteryStateRaw = 0;
            BatteryFlagsRaw = 0;
            RaiseCommandState();
            SetStatus(L("MsgDisconnected", "Disconnected."));
                UpdateConnectionState();
        }

        private async Task StopAsync()
        {
            _pollingCts?.Cancel();
            await Task.Delay(10);
        }

        private async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            // Firmware modbus_ns map exposes live registers at 0..27.
            ushort[] partA = await _modbusService.ReadHoldingRegistersAsync(0, 10, cancellationToken);
            ushort[] partB = await _modbusService.ReadHoldingRegistersAsync(10, 10, cancellationToken);
            ushort[] partC = await _modbusService.ReadHoldingRegistersAsync(20, 8, cancellationToken);
            ushort[] data = partA.Concat(partB).Concat(partC).ToArray();
            var numericValues = new double[data.Length];
            var rawValues = new long[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                long raw = DecodeByType(LiveMap[i].Type, data[i]);
                rawValues[i] = raw;
                numericValues[i] = raw;
            }

            if (TryGetRawByName(rawValues, "FW_VER", out var fwRaw))
            {
                FirmwareVersion = fwRaw.ToString(CultureInfo.InvariantCulture);
            }

            if (TryGetRawByName(rawValues, "BATTERY_STATE", out var stateRaw))
            {
                BatteryStateRaw = unchecked((byte)stateRaw);
            }

            if (TryGetRawByName(rawValues, "BATTERY_FLAGS", out var flagsRaw))
            {
                BatteryFlagsRaw = unchecked((byte)flagsRaw);
            }

            UpdateSeriesHistory(numericValues);

            var now = DateTime.Now;

            var rows = LiveMap
                .Select((item, index) =>
                {
                    return new LiveDataRow
                    {
                        Name = item.Name,
                        AddressHex = index.ToString(CultureInfo.InvariantCulture),
                        ValueType = item.Type,
                        RawValue = rawValues[index],
                        ValueText = FormatDisplayValue(item.Type, rawValues[index]),
                        Unit = item.Unit,
                        Timestamp = now
                    };
                })
                .ToArray();

            Application.Current.Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < rows.Length && i < LiveRows.Count; i++)
                {
                    LiveRows[i].Name = rows[i].Name;
                    LiveRows[i].AddressHex = rows[i].AddressHex;
                    LiveRows[i].ValueType = rows[i].ValueType;
                    LiveRows[i].RawValue = rows[i].RawValue;
                    LiveRows[i].ValueText = rows[i].ValueText;
                    LiveRows[i].Unit = rows[i].Unit;
                    LiveRows[i].Timestamp = rows[i].Timestamp;
                }
            });

            _csvLoggerService.AppendRows(CsvPath, rows);
            SetStatus(LF("MsgLastPollFmt", "Last poll: {0}", now.ToString("HH:mm:ss")));
        }

        private static bool TryGetRawByName(long[] rawValues, string symbol, out long value)
        {
            value = 0;
            if (rawValues == null || string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            int index = Array.FindIndex(LiveMap, item => string.Equals(item.Name, symbol, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= rawValues.Length)
            {
                return false;
            }

            value = rawValues[index];
            return true;
        }

        public void SetSelectedRegisters(IEnumerable<RegisterSelectionItem> leftItems, IEnumerable<RegisterSelectionItem> rightItems)
        {
            lock (_seriesLock)
            {
                _leftSelected.Clear();
                _rightSelected.Clear();

                foreach (var item in leftItems)
                {
                    if (item != null)
                    {
                        _leftSelected.Add(item.Index);
                    }
                }

                foreach (var item in rightItems)
                {
                    if (item != null)
                    {
                        _rightSelected.Add(item.Index);
                    }
                }
            }
        }

        public IReadOnlyList<SeriesSnapshot> GetSelectedSeriesSnapshots(int maxPoints)
        {
            var result = new List<SeriesSnapshot>();
            lock (_seriesLock)
            {
                foreach (var index in _leftSelected)
                {
                    Queue<double> queue;
                    if (_historyByIndex.TryGetValue(index, out queue))
                    {
                        int take = Math.Max(2, maxPoints);
                        int skip = Math.Max(0, queue.Count - take);
                        result.Add(new SeriesSnapshot
                        {
                            Index = index,
                            Name = LiveMap[index].Name,
                            IsLeftAxis = true,
                            Values = queue.Skip(skip).ToArray()
                        });
                    }
                }

                foreach (var index in _rightSelected)
                {
                    Queue<double> queue;
                    if (_historyByIndex.TryGetValue(index, out queue))
                    {
                        int take = Math.Max(2, maxPoints);
                        int skip = Math.Max(0, queue.Count - take);
                        result.Add(new SeriesSnapshot
                        {
                            Index = index,
                            Name = LiveMap[index].Name,
                            IsLeftAxis = false,
                            Values = queue.Skip(skip).ToArray()
                        });
                    }
                }
            }

            return result;
        }

        private void UpdateSeriesHistory(double[] values)
        {
            lock (_seriesLock)
            {
                int keep = Math.Max(20, LatestCount);
                for (int i = 0; i < values.Length; i++)
                {
                    Queue<double> queue;
                    if (!_historyByIndex.TryGetValue(i, out queue))
                    {
                        queue = new Queue<double>();
                        _historyByIndex[i] = queue;
                    }

                    queue.Enqueue(values[i]);
                    while (queue.Count > keep)
                    {
                        queue.Dequeue();
                    }
                }
            }
        }

        private async Task ReadAllParametersAsync()
        {
            _isBusy = true;
            RaiseCommandState();

            try
            {
                await EnsureDataFlashKeyAsync().ConfigureAwait(false);

                foreach (var row in ParameterRows)
                {
                    row.Value = await ReadParameterValueAsync(row, CancellationToken.None).ConfigureAwait(false);
                    row.ValueText = FormatDisplayValue(row.Type, row.Value);
                }

                SetStatus(L("MsgDfReadCompleted", "DF read completed."));
            }
            finally
            {
                _isBusy = false;
                RaiseCommandState();
            }
        }

        private async Task WriteAllParametersAsync()
        {
            var confirm = MessageBox.Show(
                L("MsgDfWriteConfirm", "Write all parameter values to BMS DataFlash?"),
                L("TitleDfWriteConfirm", "DF Write Confirm"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            _isBusy = true;
            RaiseCommandState();

            try
            {
                await EnsureDataFlashKeyAsync().ConfigureAwait(false);

                foreach (var row in ParameterRows)
                {
                    SyncValueFromText(row);
                    await WriteParameterValueAsync(row, CancellationToken.None).ConfigureAwait(false);

                    // Readback verify immediately to catch write failures.
                    row.Value = await ReadParameterValueAsync(row, CancellationToken.None).ConfigureAwait(false);
                    row.ValueText = FormatDisplayValue(row.Type, row.Value);
                }

                SetStatus(L("MsgDfWriteCompleted", "DF write completed (with readback verify)."));
            }
            finally
            {
                _isBusy = false;
                RaiseCommandState();
            }
        }

        private async Task ReadSelectedParameterAsync()
        {
            if (SelectedParameter == null)
            {
                return;
            }

            _isBusy = true;
            RaiseCommandState();
            try
            {
                await EnsureDataFlashKeyAsync().ConfigureAwait(false);
                SelectedParameter.Value = await ReadParameterValueAsync(SelectedParameter, CancellationToken.None).ConfigureAwait(false);
                SelectedParameter.ValueText = FormatDisplayValue(SelectedParameter.Type, SelectedParameter.Value);
                SetStatus(LF("MsgDfSingleReadFmt", "DF single read completed: {0}", SelectedParameter.Name));
            }
            finally
            {
                _isBusy = false;
                RaiseCommandState();
            }
        }

        private async Task WriteSelectedParameterAsync()
        {
            if (SelectedParameter == null)
            {
                return;
            }

            _isBusy = true;
            RaiseCommandState();
            try
            {
                await EnsureDataFlashKeyAsync().ConfigureAwait(false);
                SyncValueFromText(SelectedParameter);
                await WriteParameterValueAsync(SelectedParameter, CancellationToken.None).ConfigureAwait(false);
                SelectedParameter.Value = await ReadParameterValueAsync(SelectedParameter, CancellationToken.None).ConfigureAwait(false);
                SelectedParameter.ValueText = FormatDisplayValue(SelectedParameter.Type, SelectedParameter.Value);
                SetStatus(LF("MsgDfSingleWriteFmt", "DF single write completed: {0}", SelectedParameter.Name));
            }
            finally
            {
                _isBusy = false;
                RaiseCommandState();
            }
        }

        private Task LoadDfFromCsvAsync()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = ".csv",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    FileName = Path.GetFileName(string.IsNullOrWhiteSpace(DfCsvPath) ? "params_table.csv" : DfCsvPath)
                };

                string dir = Path.GetDirectoryName(DfCsvPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }

                if (dialog.ShowDialog() != true)
                {
                    var canceled = L("MsgDfCsvLoadCanceled", "Load CSV canceled.");
                    DfCsvStatusText = canceled;
                    SetStatus(canceled);
                    return;
                }

                var selectedPath = dialog.FileName;
                var newRows = LoadDataFlashDefinitionsFromPath(selectedPath);
                if (newRows.Count == 0)
                {
                    var empty = L("ErrDfCsvEmpty", "DF CSV is empty or no valid rows.");
                    DfCsvStatusText = empty;
                    SetStatus(empty);
                    return;
                }

                ParameterRows.Clear();
                foreach (var row in newRows)
                {
                    ParameterRows.Add(row);
                }

                DfCsvPath = selectedPath;
                ParameterRowsView.Refresh();
                var loaded = LF("MsgDfCsvLoadedFmt", "DF CSV loaded: {0} items.", newRows.Count);
                DfCsvStatusText = loaded;
                SetStatus(loaded);
            });

            return Task.CompletedTask;
        }

        private List<ParameterRow> LoadDataFlashDefinitionsFromPath(string path)
        {
            var defs = new List<ParameterRow>();
            if (!File.Exists(path))
            {
                return defs;
            }

            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                string symbol = parts[0].Trim();
                if (symbol.StartsWith("NOF_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseAddress(parts[1].Trim(), out ushort address))
                {
                    continue;
                }

                if (address >= 0x8000)
                {
                    continue;
                }

                string type = NormalizeType(parts[2]);
                long value = ParseValueByType(type, parts[3]);
                defs.Add(new ParameterRow
                {
                    Name = symbol,
                    Address = address,
                    Type = type,
                    Value = value,
                    ValueText = FormatDisplayValue(type, value),
                });
            }

            return defs.OrderBy(d => d.Address).ToList();
        }

        private Task SaveDfToCsvAsync()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var path = (DfCsvPath ?? string.Empty).Trim();

                // Check if file exists and prompt for overwrite
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var result = MessageBox.Show(
                        L("MsgDfCsvOverwriteConfirm", "File already exists. Do you want to overwrite it?"),
                        L("TitleDfCsvConfirm", "Save CSV"),
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        SaveDfCsvFile(path);
                        return;
                    }
                    else if (result == MessageBoxResult.Cancel)
                    {
                        return;
                    }
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = ".csv",
                    AddExtension = true,
                    FileName = Path.GetFileName(string.IsNullOrWhiteSpace(DfCsvPath) ? "params_table.csv" : DfCsvPath)
                };

                string dir = Path.GetDirectoryName(DfCsvPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }

                if (dialog.ShowDialog() == true)
                {
                    SaveDfCsvFile(dialog.FileName);
                }

                if (string.IsNullOrWhiteSpace(DfCsvPath))
                {
                    var canceled = L("MsgDfCsvSaveCanceled", "Save CSV canceled.");
                    DfCsvStatusText = canceled;
                    SetStatus(canceled);
                }
            });

            return Task.CompletedTask;
        }

        private void SaveDfCsvFile(string targetPath)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = new List<string>(ParameterRows.Count + 1)
            {
                "symbol,value,type,default"
            };

            foreach (var row in ParameterRows.OrderBy(r => r.Address))
            {
                SyncValueFromText(row);
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3}",
                    row.Name,
                    row.AddressHex,
                    row.Type,
                    FormatValueByType(row.Type, row.Value)));
            }

            File.WriteAllLines(targetPath, lines);
            DfCsvPath = targetPath;
            var saved = LF("MsgDfCsvSavedFmt", "DF CSV saved: {0}", targetPath);
            DfCsvStatusText = saved;
            SetStatus(saved);
        }

        private Task LoadLogForPlotAsync()
        {
            string path = (CsvPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new InvalidOperationException(L("ErrLogCsvNotFound", "Live log CSV file not found."));
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length <= 1)
            {
                throw new InvalidOperationException(L("ErrLogCsvEmpty", "Live log CSV is empty."));
            }

            var history = new Dictionary<int, Queue<double>>();
            DateTime lastTs = DateTime.MinValue;
            var lastRaw = new Dictionary<int, long>();

            foreach (var line in lines.Skip(1))
            {
                var cols = ParseCsvLine(line);
                if (cols.Count < 5)
                {
                    continue;
                }

                if (!int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                {
                    continue;
                }

                if (index < 0 || index >= LiveMap.Length)
                {
                    continue;
                }

                long raw = ParseDisplayInput(LiveMap[index].Type, cols[3]);

                if (!history.TryGetValue(index, out var q))
                {
                    q = new Queue<double>();
                    history[index] = q;
                }

                q.Enqueue(raw);
                int keep = Math.Max(20, LatestCount);
                while (q.Count > keep)
                {
                    q.Dequeue();
                }

                lastRaw[index] = raw;
                if (DateTime.TryParseExact(cols[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
                {
                    if (ts > lastTs)
                    {
                        lastTs = ts;
                    }
                }
            }

            lock (_seriesLock)
            {
                _historyByIndex.Clear();
                foreach (var kv in history)
                {
                    _historyByIndex[kv.Key] = kv.Value;
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < LiveRows.Count; i++)
                {
                    if (!lastRaw.TryGetValue(i, out var raw))
                    {
                        continue;
                    }

                    LiveRows[i].RawValue = raw;
                    LiveRows[i].ValueType = LiveMap[i].Type;
                    LiveRows[i].ValueText = FormatDisplayValue(LiveMap[i].Type, raw);
                    LiveRows[i].Timestamp = lastTs == DateTime.MinValue ? DateTime.Now : lastTs;
                }

                if (TryGetLastRawByName(lastRaw, "FW_VER", out var fwRaw))
                {
                    FirmwareVersion = fwRaw.ToString(CultureInfo.InvariantCulture);
                }

                if (TryGetLastRawByName(lastRaw, "BATTERY_STATE", out var stateRaw))
                {
                    BatteryStateRaw = unchecked((byte)stateRaw);
                }

                if (TryGetLastRawByName(lastRaw, "BATTERY_FLAGS", out var flagsRaw))
                {
                    BatteryFlagsRaw = unchecked((byte)flagsRaw);
                }
            });

            SetStatus(LF("MsgLogLoadedFmt", "Live log loaded for plot: {0}", path));
            return Task.CompletedTask;
        }

        private async Task<long> ReadParameterValueAsync(ParameterRow row, CancellationToken cancellationToken)
        {
            string type = NormalizeType(row.Type);
            if (type == "U32" || type == "F32")
            {
                uint value32 = await _modbusService.ReadDataFlashRegister32Async(row.Address, cancellationToken).ConfigureAwait(false);
                return unchecked((long)value32);
            }

            ushort value16 = await _modbusService.ReadDataFlashRegisterAsync(row.Address, cancellationToken).ConfigureAwait(false);
            switch (type)
            {
                case "I8":
                    return (sbyte)(value16 & 0xFF);
                case "I16":
                    return (short)value16;
                case "U8":
                    return value16 & 0xFF;
                default:
                    return value16;
            }
        }

        private static bool TryGetLastRawByName(IReadOnlyDictionary<int, long> lastRaw, string symbol, out long value)
        {
            value = 0;
            if (lastRaw == null || string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            int index = Array.FindIndex(LiveMap, item => string.Equals(item.Name, symbol, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            return lastRaw.TryGetValue(index, out value);
        }

        private async Task WriteParameterValueAsync(ParameterRow row, CancellationToken cancellationToken)
        {
            string type = NormalizeType(row.Type);
            if (type == "U32" || type == "F32")
            {
                await _modbusService.WriteDataFlashRegister32Async(row.Address, unchecked((uint)row.Value), cancellationToken).ConfigureAwait(false);
                return;
            }

            ushort encoded;
            switch (type)
            {
                case "I8":
                    encoded = unchecked((ushort)(byte)(sbyte)row.Value);
                    break;
                case "I16":
                    encoded = unchecked((ushort)(short)row.Value);
                    break;
                case "U8":
                    encoded = unchecked((ushort)(byte)row.Value);
                    break;
                default:
                    encoded = unchecked((ushort)row.Value);
                    break;
            }

            await _modbusService.WriteDataFlashRegisterAsync(row.Address, encoded, cancellationToken).ConfigureAwait(false);
        }

        private static string NormalizeType(string type)
        {
            return (type ?? string.Empty).Trim().ToUpperInvariant();
        }

        private long ParseValueByType(string type, string valueText)
        {
            string t = NormalizeType(type);
            string text = (valueText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(text.Substring(2), 16);
            }

            if (t == "F32")
            {
                if (text.Contains(".") || text.Contains("e") || text.Contains("E"))
                {
                    float f = float.Parse(text, CultureInfo.InvariantCulture);
                    return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
                }
            }

            return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static string FormatValueByType(string type, long value)
        {
            string t = NormalizeType(type);
            if (t == "F32")
            {
                var bytes = BitConverter.GetBytes(unchecked((uint)value));
                return BitConverter.ToSingle(bytes, 0).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private long ParseDisplayInput(string type, string input)
        {
            return ParseValueByType(type, input);
        }

        private string FormatDisplayValue(string type, long value)
        {
            if (!IsHexDisplay)
            {
                return FormatValueByType(type, value);
            }

            string t = NormalizeType(type);
            switch (t)
            {
                case "U8":
                case "I8":
                    return "0x" + ((byte)value).ToString("X2", CultureInfo.InvariantCulture);
                case "U16":
                case "I16":
                    return "0x" + ((ushort)value).ToString("X4", CultureInfo.InvariantCulture);
                case "U32":
                case "F32":
                    return "0x" + ((uint)value).ToString("X8", CultureInfo.InvariantCulture);
                default:
                    return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
            }
        }

        private static long DecodeByType(string type, ushort value16)
        {
            switch (NormalizeType(type))
            {
                case "I8":
                    return (sbyte)(value16 & 0xFF);
                case "I16":
                    return (short)value16;
                case "U8":
                    return (byte)(value16 & 0xFF);
                default:
                    return value16;
            }
        }

        private void SyncValueFromText(ParameterRow row)
        {
            if (row == null)
            {
                return;
            }

            row.Value = ParseDisplayInput(row.Type, row.ValueText);
        }

        private void RefreshParameterValueText()
        {
            if (ParameterRows == null)
            {
                return;
            }

            foreach (var row in ParameterRows)
            {
                row.ValueText = FormatDisplayValue(row.Type, row.Value);
            }
        }

        private void RefreshLiveRowsValueText()
        {
            if (LiveRows == null)
            {
                return;
            }

            foreach (var row in LiveRows)
            {
                row.ValueText = FormatDisplayValue(row.ValueType, row.RawValue);
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var list = new List<string>();
            if (line == null)
            {
                return list;
            }

            bool quoted = false;
            var token = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        token.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }

                    continue;
                }

                if (c == ',' && !quoted)
                {
                    list.Add(token.ToString());
                    token.Clear();
                }
                else
                {
                    token.Append(c);
                }
            }

            list.Add(token.ToString());
            return list;
        }

        private async Task WriteDataFlashKeyAsync()
        {
            _isBusy = true;
            RaiseCommandState();

            try
            {
                ushort key = ParseDataFlashKey();
                await _modbusService.WriteDataFlashAccessKeyAsync(key, CancellationToken.None).ConfigureAwait(false);
                SetStatus(L("MsgDfKeyWritten", "DF key written to register 99."));
            }
            finally
            {
                _isBusy = false;
                RaiseCommandState();
            }
        }

        private async Task CheckForUpdateAsync()
        {
            _isBusy = true;
            RaiseCommandState();

            try
            {
                if (string.IsNullOrWhiteSpace(GitHubOwner) || string.IsNullOrWhiteSpace(GitHubRepo))
                {
                    throw new InvalidOperationException(L("ErrGithubRepoRequired", "Please set GitHub owner/repo first."));
                }

                SetStatus(L("MsgCheckingUpdate", "Checking updates..."));
                var release = await _updateService.GetLatestReleaseAsync(GitHubOwner, GitHubRepo, CancellationToken.None).ConfigureAwait(false);
                Version current = GetCurrentVersion();
                if (release.Version == null || release.Version <= current)
                {
                    SetStatus(L("MsgNoUpdate", "Already up to date."));
                    return;
                }

                var confirm = MessageBox.Show(
                    LF("MsgUpdateAvailableFmt", "New version {0} is available. Download and install now?", release.TagName),
                    L("TitleUpdateAvailable", "Update Available"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (confirm != MessageBoxResult.Yes)
                {
                    SetStatus(L("MsgUpdateSkipped", "Update skipped."));
                    return;
                }

                string installerPath;
                if (!string.IsNullOrWhiteSpace(release.InstallerAssetUrl))
                {
                    SetStatus(L("MsgDownloadingUpdate", "Downloading update installer..."));
                    installerPath = await _updateService.DownloadInstallerAsync(release.InstallerAssetUrl, CancellationToken.None).ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
                {
                    Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
                    SetStatus(L("MsgOpenedReleasePage", "Opened release page for manual download."));
                    return;
                }
                else
                {
                    throw new InvalidOperationException(L("ErrUpdateAssetMissing", "No downloadable installer found in latest release."));
                }

                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                SetStatus(L("MsgUpdateInstallerStarted", "Update installer started."));
            }
            finally
            {
                _isBusy = false;
                RaiseCommandState();
            }
        }

        private static Version GetCurrentVersion()
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver ?? new Version(0, 0, 0, 0);
        }

        private async Task EnsureDataFlashKeyAsync()
        {
            ushort key = ParseDataFlashKey();
            await _modbusService.WriteDataFlashAccessKeyAsync(key, CancellationToken.None).ConfigureAwait(false);
        }

        private ushort ParseDataFlashKey()
        {
            string text = (DataFlashKeyText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(L("ErrDfKeyRequired", "Please input DF key first."));
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private IEnumerable<ParameterRow> LoadDataFlashDefinitions()
        {
            return LoadDataFlashDefinitionsFromPath(ResolveTemplateCsvPath());
        }

        private static bool TryParseAddress(string text, out ushort address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
            }

            return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out address);
        }

        private static string ResolveTemplateCsvPath()
        {
            string current = Environment.CurrentDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
            {
                string candidate = Path.Combine(current, "docs", "params_table.csv");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }

            return Path.Combine(Environment.CurrentDirectory, "params_table.csv");
        }

        private void SaveAppSettings()
        {
            try
            {
                _appSettingsService.Save(new AppSettingsService.AppSettings
                {
                    GitHubOwner = GitHubOwner,
                    GitHubRepo = GitHubRepo,
                });
            }
            catch
            {
                // Ignore settings persistence failures to avoid blocking runtime operations.
            }
        }

        private bool FilterParameterRow(object obj)
        {
            var row = obj as ParameterRow;
            if (row == null)
            {
                return false;
            }

            string key = (DfFilterText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            return row.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                || row.AddressHex.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                || (row.Type != null && row.Type.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RefreshParameterFilter()
        {
            if (Application.Current == null)
            {
                ParameterRowsView?.Refresh();
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                ParameterRowsView?.Refresh();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => ParameterRowsView?.Refresh());
            }
        }

        private void RefreshPorts()
        {
            var current = SelectedPort;
            var ports = SerialPort.GetPortNames().OrderBy(x => x).ToArray();

            Ports.Clear();
            foreach (var port in ports)
            {
                Ports.Add(port);
            }

            if (!string.IsNullOrWhiteSpace(current) && Ports.Contains(current))
            {
                SelectedPort = current;
            }
            else
            {
                SelectedPort = Ports.FirstOrDefault();
            }

            OnPropertyChanged(nameof(SelectedPort));
            SetStatus(L("MsgPortsRefreshed", "Ports refreshed."));
        }

        private void BrowseCsvPath()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = Path.GetFileName(string.IsNullOrWhiteSpace(CsvPath) ? "bms_live.csv" : CsvPath)
            };

            string dir = Path.GetDirectoryName(CsvPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                dialog.InitialDirectory = dir;
            }

            if (dialog.ShowDialog() == true)
            {
                CsvPath = dialog.FileName;
                SetStatus(L("MsgLogCsvPathSelected", "Log CSV path selected."));
            }
        }

        private void BrowseDfCsvPath()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = Path.GetFileName(string.IsNullOrWhiteSpace(DfCsvPath) ? "params_table.csv" : DfCsvPath)
            };

            string dir = Path.GetDirectoryName(DfCsvPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                dialog.InitialDirectory = dir;
            }

            if (dialog.ShowDialog() == true)
            {
                DfCsvPath = dialog.FileName;
                SetStatus(L("MsgDfCsvPathSelected", "DF CSV path selected."));
            }
        }

        private void RunCommand(Func<Task> command)
        {
            _ = RunCommandCoreAsync(command);
        }

        private async Task RunCommandCoreAsync(Func<Task> command)
        {
            try
            {
                await command().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SetStatus(LF("MsgErrorFmt", "Error: {0}", ex.Message));
                    MessageBox.Show(ex.Message, L("TitleHostUiError", "Host UI Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private static string L(string key, string fallback)
        {
            if (Application.Current == null)
            {
                return fallback;
            }

            var value = Application.Current.TryFindResource(key) as string;
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            string format = L(key, fallback);
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }

        private void SetStatus(string text)
        {
            if (Application.Current == null)
            {
                StatusText = text;
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                StatusText = text;
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => StatusText = text);
            }
        }

        private void RaiseCommandState()
        {
            void Raise()
            {
                (ConnectToggleCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ReadParamsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (WriteParamsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ReadSelectedParamCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (WriteSelectedParamCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoadDfCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SaveDfCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoadLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (BrowseCsvPathCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (BrowseDfCsvPathCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (WriteKeyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }

            if (Application.Current == null)
            {
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                Raise();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(Raise);
            }
        }

        private void UpdateConnectionState()
        {
            ConnectButtonText = _modbusService.IsConnected ? "Disconnect" : "Connect";
            (ConnectToggleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
