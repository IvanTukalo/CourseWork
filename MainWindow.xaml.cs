using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CourseWork
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public const double SIMULATION_SPEED_FACTOR = 1.0 / 6.0;
        public const double UPDATE_INTERVAL_SECONDS = 0.5;

        private DispatcherTimer _simulationTimer;
        private TimeSpan _currentSimTime;
        public TimeSpan CurrentSimTime
        {
            get => _currentSimTime;
            set { _currentSimTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentSimTimeString)); }
        }
        public string CurrentSimTimeString => $"{_currentSimTime.Hours:D2}:{_currentSimTime.Minutes:D2}:{_currentSimTime.Seconds:D2}";

        private bool _isElectricityOn = true;
        public bool IsElectricityOn
        {
            get => _isElectricityOn;
            set { _isElectricityOn = value; OnPropertyChanged(); LogEvent($"Електроенергію {(value ? "увімкнено" : "вимкнено")}"); UpdateAllDeviceStatesAfterPowerChange(); }
        }

        private ToolType _selectedTool = ToolType.None;
        private Button _selectedToolButton = null;

        public ObservableCollection<DeviceViewModel> DevicesOnPlan { get; set; }
        public ObservableCollection<RoomAreaViewModel> RoomAreas { get; set; }
        public ObservableCollection<LogEntry> LogEntries { get; set; }

        private SelectedRoomInfoViewModel _selectedRoomInfo;
        public SelectedRoomInfoViewModel SelectedRoomInfo
        {
            get => _selectedRoomInfo;
            set { _selectedRoomInfo = value; OnPropertyChanged(); }
        }

        private List<SmartDeviceBase> _allDevices;
        private List<Room> _allRooms;

        public MainWindow()
        {
            // 1. Спочатку ініціалізуємо всі колекції та ViewModel
            DevicesOnPlan = new ObservableCollection<DeviceViewModel>();
            RoomAreas = new ObservableCollection<RoomAreaViewModel>();
            LogEntries = new ObservableCollection<LogEntry>();
            SelectedRoomInfo = new SelectedRoomInfoViewModel(this);
            this.DataContext = this; // DataContext теж краще встановити до

            // 2. Тепер викликаємо InitializeComponent.
            // Навіть якщо він викличе подію, DevicesOnPlan вже не буде null.
            InitializeComponent();

            // 3. Виконуємо решту налаштувань
            InitializeSimulation();
            InitializeDevicesAndRooms();
            UpdateDeviceFilters();
        }

        private void InitializeSimulation()
        {
            CurrentSimTime = TimeSpan.Zero;
            _simulationTimer = new DispatcherTimer();
            _simulationTimer.Interval = TimeSpan.FromSeconds(UPDATE_INTERVAL_SECONDS);
            _simulationTimer.Tick += SimulationTimer_Tick;
        }

        private void InitializeDevicesAndRooms()
        {
            _allDevices = new List<SmartDeviceBase>();
            _allRooms = new List<Room>();

            // Створення кімнат (координати та розміри - приклади, налаштуйте під ваш HousePlan.jpg)
            // Кожна кімната отримує посилання на MainWindow для доступу до глобальних параметрів симуляції, якщо потрібно
            var porch = new Room("Крильце", new Rect(10, 10, 100, 80), this) { DefaultTemperature = 18, DefaultHumidity = 50 };
            var tambour = new Room("Тамбур", new Rect(120, 10, 80, 80), this) { DefaultTemperature = 20, DefaultHumidity = 50 };
            var hall = new Room("Хол", new Rect(210, 10, 150, 120), this) { DefaultTemperature = 22, DefaultHumidity = 45 };
            var bedroom1 = new Room("Спальня 1", new Rect(10, 100, 180, 100), this) { DefaultTemperature = 22, DefaultHumidity = 50 };
            var bedroom2 = new Room("Спальня 2", new Rect(10, 210, 180, 100), this) { DefaultTemperature = 22, DefaultHumidity = 50 };
            var livingRoom = new Room("Вітальня", new Rect(210, 140, 150, 170), this) { DefaultTemperature = 23, DefaultHumidity = 45 };
            var kitchen = new Room("Кухня", new Rect(370, 10, 120, 150), this) { DefaultTemperature = 21, DefaultHumidity = 55 };
            var bathroom = new Room("Ванна", new Rect(370, 170, 120, 100), this) { DefaultTemperature = 24, DefaultHumidity = 60 };
            _allRooms.AddRange(new[] { porch, tambour, hall, bedroom1, bedroom2, livingRoom, kitchen, bathroom });

            foreach (var room in _allRooms)
            {
                RoomAreas.Add(new RoomAreaViewModel(room.Name, room.AreaRect));
            }

            // Створення пристроїв (кожен пристрій отримує посилання на MainWindow)
            // Windows (В)
            _allDevices.Add(new WindowDevice("В1", "W001", bathroom, new Point(380, 180), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В2", "W002", bedroom1, new Point(20, 110), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В3", "W003", bedroom1, new Point(160, 110), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В4", "W004", bedroom2, new Point(20, 220), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В5", "W005", bedroom2, new Point(160, 220), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В6", "W006", livingRoom, new Point(220, 150), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В7", "W007", livingRoom, new Point(330, 150), new Size(30, 10), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В8", "W008", kitchen, new Point(380, 130), new Size(30, 10), this) { DeviceType = DeviceType.Window });


            _allDevices.Add(new MotionSensorLamp("Л1", "L001", porch, new Point(20, 20), new Size(15, 15), this) { DeviceType = DeviceType.Lamp, ActivationDurationInSimMinutes = 12 });
            _allDevices.Add(new MotionSensorLamp("Л2", "L002", tambour, new Point(130, 50), new Size(15, 15), this) { DeviceType = DeviceType.Lamp, ActivationDurationInSimMinutes = 12 });


            _allDevices.Add(new CameraDevice("К1", "C001", porch, new Point(50, 20), new Size(10, 10), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К2", "C002", hall, new Point(220, 20), new Size(10, 10), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К3", "C003", bedroom1, new Point(50, 180), new Size(10, 10), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К4", "C004", bedroom2, new Point(50, 280), new Size(10, 10), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К5", "C005", livingRoom, new Point(280, 280), new Size(10, 10), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });

            _allDevices.Add(new FireSprinklerDevice("ВП1", "FS001", tambour, new Point(130, 20), new Size(15, 15), 0.80, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП2", "FS002", hall, new Point(250, 80), new Size(15, 15), 0.60, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП3", "FS003", hall, new Point(330, 80), new Size(15, 15), 0.75, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП4", "FS004", bedroom1, new Point(30, 140), new Size(15, 15), 0.50, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП5", "FS005", bedroom1, new Point(170, 140), new Size(15, 15), 0.90, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП6", "FS006", bedroom2, new Point(30, 250), new Size(15, 15), 0.60, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП7", "FS007", bedroom2, new Point(170, 250), new Size(15, 15), 0.85, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП8", "FS008", kitchen, new Point(380, 20), new Size(15, 15), 1.00, this, true) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП9", "FS009", livingRoom, new Point(230, 200), new Size(15, 15), 0.90, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП10", "FS010", livingRoom, new Point(340, 200), new Size(15, 15), 0.95, this) { DeviceType = DeviceType.FireSystem });

            _allDevices.Add(new ThermostatDevice("Т1", "TH001", bedroom1, new Point(150, 150), new Size(20, 10), this) { DeviceType = DeviceType.Thermostat });
            _allDevices.Add(new ThermostatDevice("Т2", "TH002", bedroom2, new Point(150, 260), new Size(20, 10), this) { DeviceType = DeviceType.Thermostat });
            _allDevices.Add(new ThermostatDevice("Т3", "TH003", livingRoom, new Point(280, 180), new Size(20, 10), this) { DeviceType = DeviceType.Thermostat });

            _allDevices.Add(new HeaterDevice("О1", "H001", bedroom1, new Point(150, 170), new Size(20, 10), this) { DeviceType = DeviceType.Heater });
            _allDevices.Add(new HeaterDevice("О2", "H002", bedroom2, new Point(150, 240), new Size(20, 10), this) { DeviceType = DeviceType.Heater });
            _allDevices.Add(new HeaterDevice("О3", "H003", livingRoom, new Point(280, 230), new Size(20, 10), this) { DeviceType = DeviceType.Heater });

            _allDevices.Add(new ConditionerDevice("КОН1", "AC001", bedroom1, new Point(150, 130), new Size(20, 10), this) { DeviceType = DeviceType.Conditioner });
            _allDevices.Add(new ConditionerDevice("КОН2", "AC002", bedroom2, new Point(150, 280), new Size(20, 10), this) { DeviceType = DeviceType.Conditioner });
            _allDevices.Add(new ConditionerDevice("КОН3", "AC003", livingRoom, new Point(280, 160), new Size(20, 10), this) { DeviceType = DeviceType.Conditioner });

            _allDevices.Add(new HumidifierDevice("З1", "HU001", bedroom1, new Point(120, 170), new Size(20, 10), this) { DeviceType = DeviceType.Humidifier });
            _allDevices.Add(new HumidifierDevice("З2", "HU002", bedroom2, new Point(120, 240), new Size(20, 10), this) { DeviceType = DeviceType.Humidifier });
            _allDevices.Add(new HumidifierDevice("З3", "HU003", livingRoom, new Point(250, 230), new Size(20, 10), this) { DeviceType = DeviceType.Humidifier });

            _allDevices.Add(new DehumidifierDevice("ОС1", "DH001", bedroom1, new Point(120, 130), new Size(20, 10), this) { DeviceType = DeviceType.Dehumidifier });
            _allDevices.Add(new DehumidifierDevice("ОС2", "DH002", bedroom2, new Point(120, 280), new Size(20, 10), this) { DeviceType = DeviceType.Dehumidifier });
            _allDevices.Add(new DehumidifierDevice("ОС3", "DH003", livingRoom, new Point(250, 160), new Size(20, 10), this) { DeviceType = DeviceType.Dehumidifier });

            _allDevices.Add(new ChandelierDevice("ЛЮ1", "CH001", hall, new Point(250, 50), new Size(25, 25), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ2", "CH002", bedroom1, new Point(90, 150), new Size(25, 25), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ3", "CH003", bedroom2, new Point(90, 260), new Size(25, 25), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ4", "CH004", livingRoom, new Point(280, 250), new Size(25, 25), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ5", "CH005", kitchen, new Point(420, 80), new Size(25, 25), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ6", "CH006", bathroom, new Point(420, 230), new Size(25, 25), this) { DeviceType = DeviceType.Chandelier });

            _allDevices.Add(new FanDevice("ВЕ1", "F001", bathroom, new Point(400, 200), new Size(15, 15), this, true) { DeviceType = DeviceType.Fan });
            _allDevices.Add(new FanDevice("ВЕ2", "F002", kitchen, new Point(400, 50), new Size(15, 15), this, false) { DeviceType = DeviceType.Fan });

            _allDevices.Add(new SolarPanelDevice("СП1", "SP001", porch, new Point(70, 50), new Size(30, 15), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new SolarPanelDevice("СП2", "SP002", porch, new Point(70, 70), new Size(30, 15), this) { DeviceType = DeviceType.Special }); // Added СП2
            _allDevices.Add(new SirenDevice("С1", "SR001", hall, new Point(300, 20), new Size(15, 15), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new DoorDevice("Д1", "D001", porch, new Point(50, 70), new Size(10, 30), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new BatteryDevice("Б1", "B001", hall, new Point(320, 20), new Size(20, 10), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new ManualSwitchDevice("Перемикач ВП8", "SW001", kitchen, new Point(420, 20), new Size(10, 10), this,
                (simTime, powerOn) => {
                    var vp8 = _allDevices.OfType<FireSprinklerDevice>().FirstOrDefault(d => d.Id == "FS008");
                    vp8?.ActivateManual(simTime); // Pass current simTime
                    // LogEvent is called from within ActivateManual now
                })
            { DeviceType = DeviceType.Special });
            _allDevices.Add(new StoveDevice("Плита", "ST001", kitchen, new Point(400, 80), new Size(25, 20), this) { DeviceType = DeviceType.Special });

            foreach (var device in _allDevices)
            {
                device.AssociatedRoom?.Devices.Add(device);
                DevicesOnPlan.Add(new DeviceViewModel(device));
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Логіка сплеш-скріна тепер в App.xaml.cs.
            // Цей метод тепер лише запускає симуляцію.
            _simulationTimer.Start();
            LogEvent("Симуляцію розпочато.");
        }

        private void SimulationTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan elapsedSimTimePerTick = TimeSpan.FromMinutes(UPDATE_INTERVAL_SECONDS * 60 * SIMULATION_SPEED_FACTOR);
            CurrentSimTime += elapsedSimTimePerTick;

            bool hasSolarPower = _allDevices.OfType<SolarPanelDevice>().Any(p => p.CurrentState == DeviceState.Working);
            var mainBattery = _allDevices.OfType<BatteryDevice>().FirstOrDefault();
            bool batteryHasPower = mainBattery?.CurrentState == DeviceState.Working && mainBattery.ChargeLevel > 0;
            bool effectivePower = IsElectricityOn || hasSolarPower || (!IsElectricityOn && batteryHasPower);

            foreach (var room in _allRooms)
                room.UpdateEnvironment(elapsedSimTimePerTick, effectivePower);

            foreach (var device in _allDevices)
            {
                device.UpdateState(CurrentSimTime, effectivePower, device.AssociatedRoom);
                if (device is StoveDevice stove && stove.CurrentState == DeviceState.Working)
                    _allDevices.OfType<FanDevice>().FirstOrDefault(f => f.Id == "F002")?.ActivateManual(effectivePower);
            }

            if (mainBattery != null)
            {
                mainBattery.IsDischarging = !IsElectricityOn && !hasSolarPower;
                mainBattery.IsCharging = hasSolarPower;
            }
            SelectedRoomInfo?.Refresh();
        }
        private void UpdateAllDeviceStatesAfterPowerChange()
        {
            bool hasSolarPower = _allDevices.OfType<SolarPanelDevice>().Any(p => p.CurrentState == DeviceState.Working);
            var mainBattery = _allDevices.OfType<BatteryDevice>().FirstOrDefault();
            bool batteryHasPower = mainBattery?.CurrentState == DeviceState.Working && mainBattery.ChargeLevel > 0;
            bool effectivePower = IsElectricityOn || hasSolarPower || (!IsElectricityOn && batteryHasPower);

            foreach (var device in _allDevices)
            {
                device.UpdateState(CurrentSimTime, effectivePower, device.AssociatedRoom);
            }
            SelectedRoomInfo?.Refresh();
        }


        private void ChangeTime_Click(object sender, RoutedEventArgs e)
        {
            var newTimeStr = ShowInputDialog("Введіть новий час (ГГ:ХХ):", "Зміна часу");
            if (TimeSpan.TryParse(newTimeStr, out TimeSpan newTime))
            {
                CurrentSimTime = newTime;
                LogEvent($"Час змінено на {newTimeStr}");
            }
            else if (!string.IsNullOrEmpty(newTimeStr))
            {
                ShowCustomMessageBox("Невірний формат часу.", "Помилка");
            }
        }

        private LogWindow _logWindow;
        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logWindow == null || PresentationSource.FromVisual(_logWindow) == null) // Check if window is disposed
            {
                _logWindow = new LogWindow(LogEntries);
                _logWindow.Owner = this;
                _logWindow.Show();
            }
            else
            {
                if (_logWindow.WindowState == WindowState.Minimized) _logWindow.WindowState = WindowState.Normal;
                _logWindow.Activate();
            }
        }

        private void ToggleElectricity_Click(object sender, RoutedEventArgs e)
        {
            IsElectricityOn = !IsElectricityOn;
        }

        private void DeviceFilter_Changed(object sender, RoutedEventArgs e)
        {
            UpdateDeviceFilters();
        }

        private void ShowAllFilters_Click(object sender, RoutedEventArgs e)
        {
            SetAllCheckboxes(true);
            UpdateDeviceFilters();
        }

        private void HideAllFilters_Click(object sender, RoutedEventArgs e)
        {
            SetAllCheckboxes(false);
            UpdateDeviceFilters();
        }

        private void SetAllCheckboxes(bool isChecked)
        {
            ChkSpecial.IsChecked = isChecked; ChkWindows.IsChecked = isChecked; ChkThermostats.IsChecked = isChecked;
            ChkFireSystem.IsChecked = isChecked; ChkLamps.IsChecked = isChecked; ChkCameras.IsChecked = isChecked;
            ChkHeaters.IsChecked = isChecked; ChkHumidifiers.IsChecked = isChecked; ChkChandeliers.IsChecked = isChecked;
            ChkFans.IsChecked = isChecked; ChkConditioners.IsChecked = isChecked; ChkDehumidifiers.IsChecked = isChecked;
        }

        private void UpdateDeviceFilters()
        {
            foreach (var vm in DevicesOnPlan)
            {
                bool show = false;
                switch (vm.Device.DeviceType)
                {
                    case DeviceType.Special: show = ChkSpecial.IsChecked == true; break;
                    case DeviceType.Window: show = ChkWindows.IsChecked == true; break;
                    case DeviceType.Thermostat: show = ChkThermostats.IsChecked == true; break;
                    case DeviceType.FireSystem: show = ChkFireSystem.IsChecked == true; break;
                    case DeviceType.Lamp: show = ChkLamps.IsChecked == true; break;
                    case DeviceType.Camera: show = ChkCameras.IsChecked == true; break;
                    case DeviceType.Heater: show = ChkHeaters.IsChecked == true; break;
                    case DeviceType.Humidifier: show = ChkHumidifiers.IsChecked == true; break;
                    case DeviceType.Chandelier: show = ChkChandeliers.IsChecked == true; break;
                    case DeviceType.Fan: show = ChkFans.IsChecked == true; break;
                    case DeviceType.Conditioner: show = ChkConditioners.IsChecked == true; break;
                    case DeviceType.Dehumidifier: show = ChkDehumidifiers.IsChecked == true; break;
                    default: show = true; break;
                }
                vm.IsVisibleByUser = show;
            }
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            var clickedButton = sender as Button;
            if (clickedButton == null) return;

            string tagString = clickedButton.Tag?.ToString();
            if (string.IsNullOrEmpty(tagString)) return; // Should not happen if Tag is set in XAML

            if (tagString == "Selected") // Trying to deselect the currently selected tool
            {
                _selectedTool = ToolType.None;
                clickedButton.Tag = GetToolNameFromButton(clickedButton); // Reset Tag to its original tool name
                _selectedToolButton = null;
                LogEvent("Інструмент деактивовано.");
            }
            else if (Enum.TryParse<ToolType>(tagString, out ToolType newTool)) // Trying to select a new tool
            {
                if (_selectedToolButton != null) // Deselect previous tool
                {
                    _selectedToolButton.Tag = GetToolNameFromButton(_selectedToolButton);
                }
                _selectedTool = newTool;
                _selectedToolButton = clickedButton;
                _selectedToolButton.Tag = "Selected";
                LogEvent($"Обрано інструмент: {GetToolName(_selectedTool)}");

                if (_selectedTool == ToolType.ReloadAll) { ResetSimulationToDefaults(); DeselectTool(); }
                else if (_selectedTool == ToolType.ReloadFireSystem) { ReloadAllFireSystems(); DeselectTool(); }
            }
        }
        private string GetToolNameFromButton(Button button) // Helper to get original Tag
        {
            if (button == ToolFire) return nameof(ToolType.Fire);
            if (button == ToolMove) return nameof(ToolType.Move);
            if (button == ToolHammer) return nameof(ToolType.Hammer);
            if (button == ToolReloadAll) return nameof(ToolType.ReloadAll);
            if (button == ToolReloadFireSystem) return nameof(ToolType.ReloadFireSystem);
            return ""; // Should not happen
        }

        private void DeselectTool()
        {
            if (_selectedToolButton != null)
            {
                _selectedToolButton.Tag = GetToolNameFromButton(_selectedToolButton);
            }
            _selectedTool = ToolType.None;
            _selectedToolButton = null;
        }

        private void Device_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe && fe.DataContext is DeviceViewModel dvm)) return;
            SmartDeviceBase device = dvm.Device;

            // Якщо клікнули на пристрій, знімаємо виділення кімнати (якщо було)
            if (SelectedRoomInfo.IsRoomSelected)
            {
                var roomVm = RoomAreas.FirstOrDefault(r => r.Name == SelectedRoomInfo.RoomName);
                if (roomVm != null) roomVm.IsSelected = false;
                SelectedRoomInfo.ClearSelection();
            }

            if (_selectedTool == ToolType.Hammer)
            {
                if (device is WindowDevice || device is DoorDevice)
                {
                    device.Interact(ToolType.Hammer, IsElectricityOn, CurrentSimTime);
                    var siren = _allDevices.OfType<SirenDevice>().FirstOrDefault();
                    if (siren != null && (device.CurrentState == DeviceState.Destroyed || device.CurrentState == DeviceState.Off))
                    {
                        siren.Activate(CurrentSimTime);
                    }
                }
                else { ShowCustomMessageBox("Молоток можна використовувати тільки на вікнах та дверях.", "Інструмент"); }
                DeselectTool();
            }
            else if (_selectedTool == ToolType.None)
            {
                if (device is IOnOffToggleable toggleable) toggleable.ToggleState(IsElectricityOn);
                else if (device is FireSprinklerDevice sprinkler && (sprinkler.CurrentState == DeviceState.EmptyWater || sprinkler.CurrentState == DeviceState.NotWorking)) sprinkler.StartRechargeCycle(CurrentSimTime);
                else if (device is ManualSwitchDevice manualSwitch) manualSwitch.Interact(ToolType.None, IsElectricityOn, CurrentSimTime);
                else ShowCustomMessageBox($"Обрано пристрій: {device.Name}. Статус: {device.CurrentStateDescription}", "Інформація про пристрій");
            }
            e.Handled = true;
        }

        private void RoomArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe && fe.DataContext is RoomAreaViewModel ravm)) return;

            Room selectedRoom = _allRooms.FirstOrDefault(r => r.Name == ravm.Name);
            if (selectedRoom == null) return;

            if (_selectedTool != ToolType.None && _selectedTool != ToolType.ReloadAll && _selectedTool != ToolType.ReloadFireSystem)
            {
                ApplyToolToRoom(selectedRoom, _selectedTool);
                DeselectTool();
            }
            else
            {
                if (SelectedRoomInfo.IsRoomSelected && SelectedRoomInfo.CurrentRoom == selectedRoom)
                {
                    SelectedRoomInfo.ClearSelection();
                    ravm.IsSelected = false;
                }
                else
                {
                    SelectedRoomInfo.SelectRoom(selectedRoom);
                    foreach (var rAreaVM in RoomAreas) rAreaVM.IsSelected = (rAreaVM == ravm);
                }
            }
            e.Handled = true;
        }

        private void ApplyToolToRoom(Room room, ToolType tool)
        {
            switch (tool)
            {
                case ToolType.Fire:
                    room.HasFire = true; // LogEvent is in Room.HasFire setter
                    break;
                case ToolType.Move:
                    room.HasMotion = true;
                    room.MotionEndTime = CurrentSimTime.Add(TimeSpan.FromMinutes(6)); // Рух на 6 симуляційних хвилин
                    LogEvent($"Інструмент 'Рухоме тіло' використано на кімнаті {room.Name}. Рух до {room.MotionEndTime:T}");
                    break;
            }
        }

        private void ResetSimulationToDefaults()
        {
            LogEvent("Виконується повна перезагрузка системи...");
            _simulationTimer.Stop();
            CurrentSimTime = TimeSpan.Zero;

            foreach (var room in _allRooms) room.ResetToDefault();
            foreach (var device in _allDevices) device.Reset();

            IsElectricityOn = true; // Має оновити стани пристроїв

            SelectedRoomInfo.ClearSelection();
            foreach (var rAreaVM in RoomAreas) rAreaVM.IsSelected = false;

            UpdateDeviceFilters();

            var tempLog = new List<LogEntry>(LogEntries);
            LogEntries.Clear();
            LogEvent("Систему скинуто до початкових значень.");
            LogEvent("Симуляцію розпочато.");
            _simulationTimer.Start();
        }


        private void ReloadAllFireSystems()
        {
            foreach (var sprinkler in _allDevices.OfType<FireSprinklerDevice>())
            {
                sprinkler.Recharge(); // LogEvent is inside Recharge
            }
        }

        public void LogEvent(string message)
        {
            string logMessage = $"{CurrentSimTimeString} - {message}";
            if (Dispatcher.CheckAccess())
            {
                LogEntries.Add(new LogEntry(DateTime.Now, logMessage));
                _logWindow?.ScrollToBottom();
            }
            else
            {
                Dispatcher.Invoke(() => {
                    LogEntries.Add(new LogEntry(DateTime.Now, logMessage));
                    _logWindow?.ScrollToBottom();
                });
            }
            Console.WriteLine(logMessage);
        }

        private DeviceViewModel _highlightedDeviceVm = null;
        private RoomAreaViewModel _highlightedRoomVm = null;

        private void Device_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeviceViewModel dvm)
            {
                if (dvm.IsVisibleByUser)
                {
                    dvm.IsHighlighted = true;
                    _highlightedDeviceVm = dvm;
                    if (_highlightedRoomVm != null) { _highlightedRoomVm.Highlight(false); _highlightedRoomVm = null; }
                }
            }
        }
        private void Device_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_highlightedDeviceVm != null) { _highlightedDeviceVm.IsHighlighted = false; _highlightedDeviceVm = null; }
        }

        private void RoomArea_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is RoomAreaViewModel ravm)
            {
                if (_highlightedDeviceVm == null || !_highlightedDeviceVm.IsVisibleByUser)
                { ravm.Highlight(true); _highlightedRoomVm = ravm; }
            }
        }
        private void RoomArea_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_highlightedRoomVm != null) { _highlightedRoomVm.Highlight(false); _highlightedRoomVm = null; }
        }
        private void HousePlanCanvas_MouseMove(object sender, MouseEventArgs e) { /* Complex priority logic if needed */ }
        private void HousePlanCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_highlightedDeviceVm != null) _highlightedDeviceVm.IsHighlighted = false;
            if (_highlightedRoomVm != null) _highlightedRoomVm.Highlight(false);
            _highlightedDeviceVm = null; _highlightedRoomVm = null;
        }

        private static readonly Regex _numericRegex = new Regex("[^0-9.-]+");
        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (e.Text == "." && textBox != null && textBox.Text.Contains(".")) { e.Handled = true; return; }
            if (e.Text == "-" && textBox != null && (textBox.Text.Contains("-") || textBox.CaretIndex != 0)) { e.Handled = true; return; }
            e.Handled = _numericRegex.IsMatch(e.Text);
        }

        private void TemperatureInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (SelectedRoomInfo != null && sender is TextBox tb)
            {
                if (double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double temp))
                    SelectedRoomInfo.Temperature = Math.Max(0, Math.Min(40, temp));
                tb.Text = SelectedRoomInfo.Temperature.ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        private void HumidityInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (SelectedRoomInfo != null && sender is TextBox tb)
            {
                if (int.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out int humidityVal))
                    SelectedRoomInfo.Humidity = Math.Max(0, Math.Min(100, humidityVal));
                tb.Text = SelectedRoomInfo.Humidity.ToString("F0", CultureInfo.InvariantCulture);
            }
        }

        private string GetToolName(ToolType tool)
        {
            switch (tool)
            {
                case ToolType.Fire: return "Пожежа";
                case ToolType.Move: return "Рухоме тіло";
                case ToolType.Hammer: return "Молоток";
                case ToolType.ReloadAll: return "Перезагрузка";
                case ToolType.ReloadFireSystem: return "Перезарядка ВП";
                default: return "Немає";
            }
        }

        public string ShowInputDialog(string question, string caption)
        {
            var inputDialog = new InputDialog(question, caption); inputDialog.Owner = this;
            return inputDialog.ShowDialog() == true ? inputDialog.Answer : null;
        }

        private Action _messageBoxOkAction, _messageBoxYesAction, _messageBoxNoAction;
        public void ShowCustomMessageBox(string message, string title, MessageType type = MessageType.Info, Action onOk = null, Action onYes = null, Action onNo = null)
        {
            MessageBoxTitle.Text = title; MessageBoxText.Text = message;
            MessageBoxOkButton.Visibility = Visibility.Collapsed; MessageBoxYesButton.Visibility = Visibility.Collapsed; MessageBoxNoButton.Visibility = Visibility.Collapsed;
            _messageBoxOkAction = onOk; _messageBoxYesAction = onYes; _messageBoxNoAction = onNo;
            if (type == MessageType.Info || type == MessageType.Error) MessageBoxOkButton.Visibility = Visibility.Visible;
            else if (type == MessageType.Confirmation) { MessageBoxYesButton.Visibility = Visibility.Visible; MessageBoxNoButton.Visibility = Visibility.Visible; }
            CustomMessageBox.Visibility = Visibility.Visible;
        }
        private void MessageBoxOkButton_Click(object sender, RoutedEventArgs e) { CustomMessageBox.Visibility = Visibility.Collapsed; _messageBoxOkAction?.Invoke(); }
        private void MessageBoxYesButton_Click(object sender, RoutedEventArgs e) { CustomMessageBox.Visibility = Visibility.Collapsed; _messageBoxYesAction?.Invoke(); }
        private void MessageBoxNoButton_Click(object sender, RoutedEventArgs e) { CustomMessageBox.Visibility = Visibility.Collapsed; _messageBoxNoAction?.Invoke(); }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ЗАЛИШТЕ ВСІ КОНВЕРТЕРИ ТА ІНШІ ДОПОМІЖНІ КЛАСИ ТУТ, В КІНЦІ ФАЙЛУ MainWindow.xaml.cs,
    // АБО СТВОРІТЬ ДЛЯ НИХ ОКРЕМІ ФАЙЛИ .cs В ТОМУ Ж ПРОЕКТІ ТА ПРОСТОРІ ІМЕН "CourseWork".
    // ГОЛОВНЕ - ВОНИ МАЮТЬ БУТИ PUBLIC І В ПРОСТОРІ ІМЕН CourseWork.

    public enum DeviceState { Off, Working, NotWorking, Active, Inactive, Destroyed, EmptyWater, NeedsRecharge, Charging }
    public enum ToolType { None, Fire, Move, Hammer, ReloadAll, ReloadFireSystem }
    public enum DeviceType { Unknown, Window, Lamp, Camera, FireSystem, Thermostat, Heater, Conditioner, Humidifier, Dehumidifier, Chandelier, Fan, Special }
    public enum MessageType { Info, Error, Confirmation }

    public class DeviceViewModel : INotifyPropertyChanged
    {
        public SmartDeviceBase Device { get; }
        public string Name => Device.Name;
        public DeviceState State => Device.CurrentState;
        private bool _isVisibleByUser = true;
        public bool IsVisibleByUser { get => _isVisibleByUser; set { _isVisibleByUser = value; OnPropertyChanged(); } }
        private bool _isHighlighted = false;
        public bool IsHighlighted { get => _isHighlighted; set { _isHighlighted = value; OnPropertyChanged(); } }
        public Point Position => Device.Position;
        public Size Size => Device.Size;
        public bool IsThermostat => Device is ThermostatDevice;
        public double TargetTemperature
        {
            get => (Device as ThermostatDevice)?.TargetTemperature ?? 0;
            set { if (Device is ThermostatDevice td) { td.TargetTemperature = value; OnPropertyChanged(); } }
        }
        public int TargetHumidity
        {
            get => (Device as ThermostatDevice)?.TargetHumidity ?? 0;
            set { if (Device is ThermostatDevice td) { td.TargetHumidity = value; OnPropertyChanged(); } }
        }
        public DeviceViewModel(SmartDeviceBase device)
        {
            Device = device;
            Device.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SmartDeviceBase.CurrentState))
                { OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(CurrentStateDescription)); }
                if (e.PropertyName == nameof(ThermostatDevice.TargetTemperature)) OnPropertyChanged(nameof(TargetTemperature));
                if (e.PropertyName == nameof(ThermostatDevice.TargetHumidity)) OnPropertyChanged(nameof(TargetHumidity));
            };
        }
        public string CurrentStateDescription => Device.CurrentStateDescription;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RoomAreaViewModel : INotifyPropertyChanged
    {
        public string Name { get; }
        public Rect Position { get; }
        public Size Size => new Size(Position.Width, Position.Height);
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; Highlight(value); OnPropertyChanged(); } }
        private Brush _borderBrush = Brushes.Transparent;
        public Brush BorderBrush { get => _borderBrush; set { _borderBrush = value; OnPropertyChanged(); } }
        private double _borderThickness = 0;
        public double BorderThickness { get => _borderThickness; set { _borderThickness = value; OnPropertyChanged(); } }
        public RoomAreaViewModel(string name, Rect areaRect) { Name = name; Position = areaRect; }
        public void Highlight(bool highlight) { BorderBrush = highlight ? Brushes.Red : Brushes.Transparent; BorderThickness = highlight ? 2 : 0; }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SelectedRoomInfoViewModel : INotifyPropertyChanged
    {
        private readonly MainWindow _mainWindow;
        private Room _currentRoom;
        public Room CurrentRoom => _currentRoom;
        private bool _isRoomSelected;
        public bool IsRoomSelected { get => _isRoomSelected; set { _isRoomSelected = value; OnPropertyChanged(); } }
        public string RoomName => _currentRoom?.Name ?? "N/A";
        public bool HasFire
        {
            get => _currentRoom?.HasFire ?? false;
            set { if (_currentRoom != null) { _currentRoom.HasFire = value; OnPropertyChanged(); OnPropertyChanged(nameof(FireStatusText)); /* LogEvent is in Room's setter */ } }
        }
        public string FireStatusText => HasFire ? "Присутній" : "Відсутній";
        public bool CanControlFire => _currentRoom != null;
        public string BreakInStatusText => _currentRoom?.IsBreached ?? false ? "Присутній (Вікно/Двері зламано)" : "Відсутній";
        public double Temperature { get => _currentRoom?.CurrentTemperature ?? 0; set { if (_currentRoom != null) { _currentRoom.CurrentTemperature = value; OnPropertyChanged(); OnPropertyChanged(nameof(TemperatureString)); } } }
        public string TemperatureString { get => Temperature.ToString("F1", CultureInfo.InvariantCulture); set { if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double temp)) Temperature = Math.Max(0, Math.Min(40, temp)); OnPropertyChanged(); } }
        public int Humidity { get => _currentRoom?.CurrentHumidity ?? 0; set { if (_currentRoom != null) { _currentRoom.CurrentHumidity = value; OnPropertyChanged(); OnPropertyChanged(nameof(HumidityString)); } } }
        public string HumidityString { get => Humidity.ToString("F0", CultureInfo.InvariantCulture); set { if (int.TryParse(value, out int hum)) Humidity = Math.Max(0, Math.Min(100, hum)); OnPropertyChanged(); } }
        public ObservableCollection<DeviceViewModel> DevicesInRoom { get; }
        public ICommand DeviceActionCommand { get; }
        public ICommand ToggleFireStatusCommand { get; }
        public ICommand IncreaseTempCommand { get; }
        public ICommand DecreaseTempCommand { get; }
        public ICommand IncreaseHumidityCommand { get; }
        public ICommand DecreaseHumidityCommand { get; }
        public ICommand IncreaseTargetTempCommand { get; }
        public ICommand DecreaseTargetTempCommand { get; }
        public ICommand IncreaseTargetHumidityCommand { get; }
        public ICommand DecreaseTargetHumidityCommand { get; }

        public SelectedRoomInfoViewModel(MainWindow mainWindow)
        {
            _mainWindow = mainWindow; DevicesInRoom = new ObservableCollection<DeviceViewModel>(); IsRoomSelected = false;
            DeviceActionCommand = new RelayCommand<DeviceViewModel>(ExecuteDeviceAction);
            ToggleFireStatusCommand = new RelayCommand(() => HasFire = !HasFire, () => CanControlFire);
            IncreaseTempCommand = new RelayCommand(() => Temperature = Math.Min(40, Temperature + 1));
            DecreaseTempCommand = new RelayCommand(() => Temperature = Math.Max(0, Temperature - 1));
            IncreaseHumidityCommand = new RelayCommand(() => Humidity = Math.Min(100, Humidity + 1));
            DecreaseHumidityCommand = new RelayCommand(() => Humidity = Math.Max(0, Humidity - 1));
            IncreaseTargetTempCommand = new RelayCommand<DeviceViewModel>(vm => { if (vm?.Device is ThermostatDevice td) vm.TargetTemperature = Math.Min(28, td.TargetTemperature + 1); });
            DecreaseTargetTempCommand = new RelayCommand<DeviceViewModel>(vm => { if (vm?.Device is ThermostatDevice td) vm.TargetTemperature = Math.Max(16, td.TargetTemperature - 1); });
            IncreaseTargetHumidityCommand = new RelayCommand<DeviceViewModel>(vm => { if (vm?.Device is ThermostatDevice td) vm.TargetHumidity = Math.Min(80, td.TargetHumidity + 1); });
            DecreaseTargetHumidityCommand = new RelayCommand<DeviceViewModel>(vm => { if (vm?.Device is ThermostatDevice td) vm.TargetHumidity = Math.Max(20, td.TargetHumidity - 1); });
        }

        // This method is needed if the button's Click event is directly wired in XAML
        // If using Command for ToggleFireStatus, this might not be directly used from XAML Click unless Command is not set.
        public void ToggleFireStatus_Click(object sender, RoutedEventArgs e) { if (CanControlFire) HasFire = !HasFire; }

        private void ExecuteDeviceAction(DeviceViewModel dvm)
        {
            if (dvm == null || _currentRoom == null) return;
            var device = dvm.Device;
            if (device is WindowDevice || device is DoorDevice)
            {
                if (device.CurrentState == DeviceState.Destroyed || device.CurrentState == DeviceState.Off) device.Repair();
                else device.Interact(ToolType.Hammer, _mainWindow.IsElectricityOn, _mainWindow.CurrentSimTime);
            }
            else if (device is IOnOffToggleable iotDevice) iotDevice.ToggleState(_mainWindow.IsElectricityOn);
            else if (device is FireSprinklerDevice sprinkler)
            {
                if (sprinkler.CurrentState == DeviceState.EmptyWater || sprinkler.CurrentState == DeviceState.NotWorking) sprinkler.StartRechargeCycle(_mainWindow.CurrentSimTime);
                else if (sprinkler.CurrentState == DeviceState.Working && sprinkler.IsManuallyActivatable) sprinkler.ActivateManual(_mainWindow.CurrentSimTime);
            }
            // LogEvent calls are now within device methods or Room property setters
            Refresh();
        }
        public void SelectRoom(Room room)
        {
            _currentRoom = room; IsRoomSelected = true; DevicesInRoom.Clear();
            if (_currentRoom != null)
                foreach (var device in _currentRoom.Devices.OrderBy(d => d.Name))
                    if (_mainWindow.DevicesOnPlan.FirstOrDefault(dvm => dvm.Device == device) is DeviceViewModel vm) DevicesInRoom.Add(vm);
            RefreshProperties();
        }
        public void ClearSelection() { _currentRoom = null; IsRoomSelected = false; DevicesInRoom.Clear(); RefreshProperties(); }
        public void Refresh()
        {
            if (_currentRoom == null || !IsRoomSelected) return;
            RefreshProperties();
            var tempDevices = new List<DeviceViewModel>(DevicesInRoom); DevicesInRoom.Clear();
            foreach (var d in tempDevices.OrderBy(x => x.Name)) DevicesInRoom.Add(d); // Re-add to force UI refresh of list
        }
        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(RoomName)); OnPropertyChanged(nameof(HasFire)); OnPropertyChanged(nameof(FireStatusText));
            OnPropertyChanged(nameof(CanControlFire)); OnPropertyChanged(nameof(BreakInStatusText)); OnPropertyChanged(nameof(Temperature));
            OnPropertyChanged(nameof(TemperatureString)); OnPropertyChanged(nameof(Humidity)); OnPropertyChanged(nameof(HumidityString));
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogEntry(DateTime timestamp, string message) { Timestamp = timestamp; Message = message; }
        public override string ToString() => Message;
    }

    public abstract class SmartDeviceBase : INotifyPropertyChanged
    {
        protected MainWindow MainWindowContext { get; }
        public string Name { get; }
        public string Id { get; }
        public Room AssociatedRoom { get; set; }
        public Point Position { get; set; }
        public Size Size { get; set; }
        public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
        private DeviceState _currentState;
        public DeviceState CurrentState { get => _currentState; protected set { if (_currentState != value) { _currentState = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentStateDescription)); } } }
        public virtual string CurrentStateDescription => CurrentState.ToString();
        protected SmartDeviceBase(string name, string id, Room room, Point position, Size size, MainWindow mainWindowContext)
        { Name = name; Id = id; AssociatedRoom = room; Position = position; Size = size; CurrentState = DeviceState.Off; MainWindowContext = mainWindowContext; }
        public abstract void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment);
        public virtual void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime) { }
        public virtual void Reset() { CurrentState = DeviceState.Off; }
        public virtual void Repair() { if (CurrentState == DeviceState.Destroyed || CurrentState == DeviceState.NotWorking) { CurrentState = DeviceState.Working; Log("Відремонтовано."); } }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected void Log(string message) => MainWindowContext?.LogEvent($"[{Name}{(AssociatedRoom != null ? $" у {AssociatedRoom.Name}" : "")}] {message}");
    }

    public interface IOnOffToggleable { void TurnOn(bool isElectricityOn); void TurnOff(); void ToggleState(bool isElectricityOn); }

    public class WindowDevice : SmartDeviceBase
    {
        public WindowDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Ціле" : (CurrentState == DeviceState.Off ? "Зруйноване" : CurrentState.ToString());
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) { }
        public override void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime)
        { if (tool == ToolType.Hammer) { CurrentState = DeviceState.Off; AssociatedRoom.IsBreached = true; /* Log is in Room.IsBreached setter */ } }
        public override void Reset() { CurrentState = DeviceState.Working; if (AssociatedRoom != null) AssociatedRoom.IsBreached = CheckRoomBreach(); }
        public override void Repair() { base.Repair(); if (AssociatedRoom != null) AssociatedRoom.IsBreached = CheckRoomBreach(); }
        private bool CheckRoomBreach() => AssociatedRoom?.Devices.Any(d => (d is WindowDevice || d is DoorDevice) && d.CurrentState == DeviceState.Off && d != this) ?? false;
    }

    public class DoorDevice : SmartDeviceBase
    {
        public DoorDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Ціле" : (CurrentState == DeviceState.Off ? "Зруйноване" : CurrentState.ToString());
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) { }
        public override void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime)
        { if (tool == ToolType.Hammer) { CurrentState = DeviceState.Off; AssociatedRoom.IsBreached = true; /* Log in Room.IsBreached */ } }
        public override void Reset() { CurrentState = DeviceState.Working; if (AssociatedRoom != null) AssociatedRoom.IsBreached = false; } // Assuming door is the only breach point initially
        public override void Repair() { base.Repair(); if (AssociatedRoom != null) AssociatedRoom.IsBreached = CheckRoomBreach(); }
        private bool CheckRoomBreach() => AssociatedRoom?.Devices.Any(d => (d is WindowDevice || d is DoorDevice) && d.CurrentState == DeviceState.Off && d != this) ?? false;
    }

    public class MotionSensorLamp : SmartDeviceBase, IOnOffToggleable
    {
        public int ActivationDurationInSimMinutes { get; set; } = 12;
        private TimeSpan _activeUntil; private bool _isManuallyOn = false;
        public MotionSensorLamp(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override string CurrentStateDescription { get { if (CurrentState == DeviceState.Active) return "Активна (датчик)"; if (CurrentState == DeviceState.Working) return "Працює (вручну)"; if (CurrentState == DeviceState.Off) return "Вимкнена"; return CurrentState.ToString(); } }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            if (!isElectricityOn && CurrentState != DeviceState.Off) { TurnOff(); return; }
            bool canWorkByTime = (Name == "Л1" && (currentTime.Hours >= 16 || currentTime.Hours < 8)) || Name != "Л1";
            if (environment?.HasMotion == true && canWorkByTime && isElectricityOn)
            { if (CurrentState != DeviceState.Active && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Active; Log("Датчик руху спрацював."); } _activeUntil = currentTime.Add(TimeSpan.FromMinutes(ActivationDurationInSimMinutes)); environment.HasMotion = false; }
            if (CurrentState == DeviceState.Active && currentTime > _activeUntil && !_isManuallyOn) TurnOff();
            if (!canWorkByTime && Name == "Л1" && CurrentState != DeviceState.Off) TurnOff();
        }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn) { CurrentState = DeviceState.Working; _isManuallyOn = true; Log("Увімкнено вручну."); } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; _isManuallyOn = false; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
        public override void Reset() { base.Reset(); _activeUntil = TimeSpan.Zero; _isManuallyOn = false; }
    }

    public class CameraDevice : SmartDeviceBase
    {
        public int ActivationDurationInSimMinutes { get; set; } = 30;
        private TimeSpan _activeUntil;
        public CameraDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override string CurrentStateDescription => CurrentState == DeviceState.Active ? "Запис (датчик)" : (CurrentState == DeviceState.Working ? "Працює" : "Вимкнена");
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            if (!isElectricityOn && CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; return; }
            if (environment?.HasMotion == true && isElectricityOn)
            { if (CurrentState != DeviceState.Active) Log("Почала запис через рух."); CurrentState = DeviceState.Active; _activeUntil = currentTime.Add(TimeSpan.FromMinutes(ActivationDurationInSimMinutes)); environment.HasMotion = false; }
            if (CurrentState == DeviceState.Active && currentTime > _activeUntil) { CurrentState = DeviceState.Off; Log("Завершила запис."); }
        }
        public override void Reset() { base.Reset(); _activeUntil = TimeSpan.Zero; }
    }

    public class FireSprinklerDevice : SmartDeviceBase
    {
        private static readonly Random _randomInternalStatic = new Random(); // Ensure one instance for all sprinklers
        public double ActivationProbability { get; }
        public bool IsManuallyActivatable { get; }
        private readonly TimeSpan _activeDurationSim = TimeSpan.FromMinutes(6);
        private TimeSpan _activeUntil; private TimeSpan _rechargeCompleteTime;
        private const int RECHARGE_DURATION_SIM_MINUTES = 60;
        public FireSprinklerDevice(string name, string id, Room room, Point position, Size size, double probability, MainWindow mw, bool manual = false)
            : base(name, id, room, position, size, mw) { ActivationProbability = probability; IsManuallyActivatable = manual; CurrentState = DeviceState.Working; }
        public override string CurrentStateDescription { get { switch (CurrentState) { case DeviceState.Working: return "Готова (повна)"; case DeviceState.Active: return "Гасіння пожежі"; case DeviceState.EmptyWater: return "Пуста (немає води)"; case DeviceState.NeedsRecharge: return "Перезарядка..."; case DeviceState.Off: return IsManuallyActivatable ? "Вимкнено (Вручну)" : "Вимкнено"; default: return CurrentState.ToString(); } } }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            if (CurrentState == DeviceState.NeedsRecharge && currentTime >= _rechargeCompleteTime) Recharge();
            if (CurrentState == DeviceState.Active && currentTime >= _activeUntil)
            { CurrentState = DeviceState.EmptyWater; Log("Закінчилась вода."); if (environment?.HasFire == true) { Log($"НЕ ЗМІГ загасити пожежу в {environment.Name}! ВИКЛИК ПОЖЕЖНИКІВ!"); AssociatedRoom?.Devices.OfType<SirenDevice>().FirstOrDefault()?.Activate(currentTime); } else if (environment != null) Log($"Пожежу в {environment.Name} загашено."); }
            if (CurrentState == DeviceState.Working && environment?.HasFire == true && !IsManuallyActivatable && isElectricityOn && _randomInternalStatic.NextDouble() < ActivationProbability) Activate(currentTime, environment);
        }
        private void Activate(TimeSpan currentTime, Room environment) { CurrentState = DeviceState.Active; _activeUntil = currentTime + _activeDurationSim; if (environment != null) environment.HasFire = false; Log($"Активовано! Гасить пожежу в {environment?.Name ?? "невідомій зоні"}."); }
        public void ActivateManual(TimeSpan currentTime) { if (CurrentState == DeviceState.Working) { CurrentState = DeviceState.Active; _activeUntil = currentTime + _activeDurationSim; if (AssociatedRoom != null) AssociatedRoom.HasFire = false; Log("Активовано вручну!"); } else Log("Неможливо активувати вручну (не готова або вже активна)."); }
        public void Recharge() { if (CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Перезаряджено."); } }
        public void StartRechargeCycle(TimeSpan currentTime) { if (CurrentState == DeviceState.EmptyWater || CurrentState == DeviceState.NotWorking) { CurrentState = DeviceState.NeedsRecharge; _rechargeCompleteTime = currentTime.Add(TimeSpan.FromMinutes(RECHARGE_DURATION_SIM_MINUTES)); Log($"Почав перезарядку. Готовність очікується о {_rechargeCompleteTime:T}."); } }
        public override void Reset() { Recharge(); _activeUntil = TimeSpan.Zero; }
        public override void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime) { if (CurrentState == DeviceState.EmptyWater || CurrentState == DeviceState.NotWorking) StartRechargeCycle(currentTime); else if (IsManuallyActivatable && CurrentState == DeviceState.Working && isElectricityOn) ActivateManual(currentTime); }
    }

    public class ThermostatDevice : SmartDeviceBase, IOnOffToggleable
    {
        private double _targetTemperature = 22;
        public double TargetTemperature { get => _targetTemperature; set { _targetTemperature = Math.Max(16, Math.Min(28, value)); OnPropertyChanged(); Log($"Цільова температура змінена на {value:F1}°C"); } }
        private int _targetHumidity = 50;
        public int TargetHumidity { get => _targetHumidity; set { _targetHumidity = (int)Math.Max(20, Math.Min(80, value)); OnPropertyChanged(); Log($"Цільова вологість змінена на {value}%"); } }
        public ThermostatDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? $"Працює (ціль: {TargetTemperature:F1}°C, {TargetHumidity}%)" : "Вимкнено";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn && CurrentState == DeviceState.Working) TurnOff(); else if (isElectricityOn && CurrentState == DeviceState.Off) TurnOn(true); }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Увімкнено."); } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
        public override void Reset() { base.Reset(); TargetTemperature = 22; TargetHumidity = 50; CurrentState = DeviceState.Working; }
    }

    public abstract class ClimateControlUnit : SmartDeviceBase, IOnOffToggleable
    {
        protected ClimateControlUnit(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState == DeviceState.Off) { CurrentState = DeviceState.Working; Log("Почав роботу."); } }
        public void TurnOff() { if (CurrentState == DeviceState.Working) { CurrentState = DeviceState.Off; Log("Припинив роботу."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Працює" : (CurrentState == DeviceState.Off ? "Вимкнено" : "Не працює (Н/Д)");
    }

    public class HeaterDevice : ClimateControlUnit
    {
        public HeaterDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); if (thermostat != null && environment != null) { if (environment.CurrentTemperature < thermostat.TargetTemperature) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); } else { if (CurrentState == DeviceState.Working) TurnOff(); } } else if (CurrentState == DeviceState.Working) TurnOff(); }
    }
    public class ConditionerDevice : ClimateControlUnit
    {
        public ConditionerDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); if (thermostat != null && environment != null) { if (environment.CurrentTemperature > thermostat.TargetTemperature) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); } else { if (CurrentState == DeviceState.Working) TurnOff(); } } else if (CurrentState == DeviceState.Working) TurnOff(); }
    }
    public class HumidifierDevice : ClimateControlUnit
    {
        public HumidifierDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); if (thermostat != null && environment != null) { if (environment.CurrentHumidity < thermostat.TargetHumidity) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); } else { if (CurrentState == DeviceState.Working) TurnOff(); } } else if (CurrentState == DeviceState.Working) TurnOff(); }
    }
    public class DehumidifierDevice : ClimateControlUnit
    {
        public DehumidifierDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); if (thermostat != null && environment != null) { if (environment.CurrentHumidity > thermostat.TargetHumidity) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); } else { if (CurrentState == DeviceState.Working) TurnOff(); } } else if (CurrentState == DeviceState.Working) TurnOff(); }
    }

    public class ChandelierDevice : SmartDeviceBase, IOnOffToggleable
    {
        public ChandelierDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Увімкнена" : "Вимкнена";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) { if (!isElectricityOn && CurrentState == DeviceState.Working) TurnOff(); }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Увімкнено."); } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
    }

    public class FanDevice : SmartDeviceBase, IOnOffToggleable
    {
        public bool IsHumidityControlled { get; }
        private const int HUMIDITY_THRESHOLD_VE1 = 60;
        public FanDevice(string name, string id, Room room, Point position, Size size, MainWindow mw, bool humidityControlled = false) : base(name, id, room, position, size, mw) { IsHumidityControlled = humidityControlled; }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Працює" : "Вимкнено";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn && CurrentState == DeviceState.Working) { TurnOff(); return; } if (IsHumidityControlled && environment != null) { if (environment.CurrentHumidity > HUMIDITY_THRESHOLD_VE1) { if (CurrentState == DeviceState.Off && isElectricityOn) TurnOn(isElectricityOn); } else { if (CurrentState == DeviceState.Working) TurnOff(); } } }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Увімкнено."); } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
        public void ActivateManual(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Активовано (плита)."); } }
    }

    public class SolarPanelDevice : SmartDeviceBase
    {
        public SolarPanelDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Генерація енергії" : "Неактивні";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { DeviceState prevState = CurrentState; if (currentTime.Hours >= 8 && currentTime.Hours < 16) CurrentState = DeviceState.Working; else CurrentState = DeviceState.Off; if (prevState != CurrentState) Log(CurrentState == DeviceState.Working ? "Почали генерацію." : "Припинили генерацію."); }
    }

    public class SirenDevice : SmartDeviceBase
    {
        private readonly TimeSpan _activationDurationSim = TimeSpan.FromMinutes(30); private TimeSpan _activeUntil;
        public SirenDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override string CurrentStateDescription => CurrentState == DeviceState.Active ? "СИРЕНА АКТИВНА!" : "Вимкнена";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) { if (CurrentState == DeviceState.Active && currentTime > _activeUntil) { CurrentState = DeviceState.Off; Log("Вимкнулась."); } }
        public void Activate(TimeSpan currentTime) { if (CurrentState != DeviceState.Active) { CurrentState = DeviceState.Active; _activeUntil = currentTime + _activationDurationSim; Log("!!! УВІМКНЕНА !!!"); } }
        public override void Reset() { base.Reset(); _activeUntil = TimeSpan.Zero; }
    }

    public class BatteryDevice : SmartDeviceBase
    {
        public double ChargeLevel { get; private set; } = 100.0;
        public bool IsCharging { get; set; } = false; public bool IsDischarging { get; set; } = false;
        private const double DISCHARGE_RATE_PER_SIM_MINUTE_EQUIVALENT = 0.5 * (6.0 / MainWindow.UPDATE_INTERVAL_SECONDS); // % per sim minute, adjusted for update rate
        private const double CHARGE_RATE_PER_SIM_MINUTE_EQUIVALENT = 1.0 * (6.0 / MainWindow.UPDATE_INTERVAL_SECONDS);
        private bool _isEffectivelyPowering = false;
        public BatteryDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        public override string CurrentStateDescription => $"Резерв: {ChargeLevel:F0}%" + (CurrentState == DeviceState.Charging ? " (Зарядка)" : (_isEffectivelyPowering ? " (Живлення)" : " (Готова)"));
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            _isEffectivelyPowering = IsDischarging && ChargeLevel > 0 && !isElectricityOn;
            DeviceState prevState = CurrentState;
            double elapsedSimMinutes = MainWindow.UPDATE_INTERVAL_SECONDS * 60 * MainWindow.SIMULATION_SPEED_FACTOR;

            if (IsCharging && ChargeLevel < 100.0)
            { ChargeLevel = Math.Min(100.0, ChargeLevel + CHARGE_RATE_PER_SIM_MINUTE_EQUIVALENT * elapsedSimMinutes); CurrentState = DeviceState.Charging; }
            else if (IsDischarging && ChargeLevel > 0 && !isElectricityOn)
            { ChargeLevel = Math.Max(0, ChargeLevel - DISCHARGE_RATE_PER_SIM_MINUTE_EQUIVALENT * elapsedSimMinutes); CurrentState = (ChargeLevel > 0) ? DeviceState.Working : DeviceState.EmptyWater; }
            else if (ChargeLevel >= 100 && IsCharging) { IsCharging = false; CurrentState = DeviceState.Working; Log("Повністю заряджена."); }
            else if (isElectricityOn && CurrentState != DeviceState.Charging) CurrentState = DeviceState.Working;

            if (ChargeLevel <= 0 && CurrentState != DeviceState.EmptyWater) { CurrentState = DeviceState.EmptyWater; Log("Розряджена."); }
            if (ChargeLevel > 0 && CurrentState == DeviceState.EmptyWater && isElectricityOn) CurrentState = DeviceState.Working; // Can recover if power returns and has some charge

            if (prevState != CurrentState && CurrentState == DeviceState.Charging && prevState != DeviceState.Charging) Log("Почала зарядку.");
            if (prevState == DeviceState.Charging && CurrentState == DeviceState.Working && ChargeLevel >= 100) { /* Logged above */ }
            else if (prevState != DeviceState.Working && CurrentState == DeviceState.Working && _isEffectivelyPowering) Log("Почала живити будинок.");
            else if (prevState == DeviceState.Working && CurrentState == DeviceState.Working && !_isEffectivelyPowering && isElectricityOn) { /* Standby */ }
        }
        public override void Reset() { ChargeLevel = 100.0; CurrentState = DeviceState.Working; IsCharging = false; IsDischarging = false; }
    }

    public class ManualSwitchDevice : SmartDeviceBase
    {
        private readonly Action<TimeSpan, bool> _onActivateWithTimeAndPower;
        public ManualSwitchDevice(string name, string id, Room room, Point position, Size size, MainWindow mw, Action<TimeSpan, bool> onActivateAction)
            : base(name, id, room, position, size, mw) { _onActivateWithTimeAndPower = onActivateAction; CurrentState = DeviceState.Working; }
        public override string CurrentStateDescription => "Перемикач";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) { /* Passive */ }
        public override void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime)
        { if (isElectricityOn) _onActivateWithTimeAndPower?.Invoke(currentTime, isElectricityOn); else Log("Неможливо активувати, немає електроенергії."); }
    }
    public class StoveDevice : SmartDeviceBase, IOnOffToggleable
    {
        public StoveDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Готує" : "Вимкнена";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) { if (!isElectricityOn && CurrentState == DeviceState.Working) TurnOff(); }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Увімкнено."); } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
    }

    public class Room : INotifyPropertyChanged
    {
        private readonly MainWindow _mainWindowContext; // Renamed for clarity
        public string Name { get; }
        public Rect AreaRect { get; }
        public List<SmartDeviceBase> Devices { get; } = new List<SmartDeviceBase>();
        private double _currentTemperature;
        public double CurrentTemperature { get => _currentTemperature; set { _currentTemperature = value; OnPropertyChanged(); } }
        public double DefaultTemperature { get; set; } = 20;
        private int _currentHumidity;
        public int CurrentHumidity { get => _currentHumidity; set { _currentHumidity = value; OnPropertyChanged(); } }
        public int DefaultHumidity { get; set; } = 50;
        private bool _hasFire;
        public bool HasFire { get => _hasFire; set { if (_hasFire != value) { _hasFire = value; OnPropertyChanged(); if (value) _mainWindowContext.LogEvent($"ПОЖЕЖА в {Name}!"); else _mainWindowContext.LogEvent($"Пожежу в {Name} ліквідовано."); } } }
        private bool _hasMotion;
        public bool HasMotion { get => _hasMotion; set { _hasMotion = value; OnPropertyChanged(); } }
        public TimeSpan MotionEndTime { get; set; }
        private bool _isBreached;
        public bool IsBreached { get => _isBreached; set { if (_isBreached != value) { _isBreached = value; OnPropertyChanged(); if (value) _mainWindowContext.LogEvent($"ВЗЛОМ в {Name}!"); else _mainWindowContext.LogEvent($"Загрозу взлому в {Name} усунуто."); } } }

        public Room(string name, Rect areaRect, MainWindow mainWindow)
        { Name = name; AreaRect = areaRect; _mainWindowContext = mainWindow; CurrentTemperature = DefaultTemperature; CurrentHumidity = DefaultHumidity; }

        public void UpdateEnvironment(TimeSpan elapsedSimTimePerTick, bool isElectricityOn)
        {
            if (HasMotion && _mainWindowContext.CurrentSimTime > MotionEndTime) HasMotion = false;

            if (isElectricityOn)
            {
                // 1°C (або 1%) за 6 симуляційних хвилин
                double changeRateMultiplier = elapsedSimTimePerTick.TotalMinutes / 6.0;

                double tempChange = 0;
                foreach (var heater in Devices.OfType<HeaterDevice>().Where(d => d.CurrentState == DeviceState.Working)) tempChange += 1.0 * changeRateMultiplier;
                foreach (var conditioner in Devices.OfType<ConditionerDevice>().Where(d => d.CurrentState == DeviceState.Working)) tempChange -= 1.0 * changeRateMultiplier;
                CurrentTemperature = Math.Round(Math.Max(0, Math.Min(50, CurrentTemperature + tempChange)), 1);

                double humidityChange = 0;
                foreach (var humidifier in Devices.OfType<HumidifierDevice>().Where(d => d.CurrentState == DeviceState.Working)) humidityChange += 1.0 * changeRateMultiplier;
                foreach (var dehumidifier in Devices.OfType<DehumidifierDevice>().Where(d => d.CurrentState == DeviceState.Working)) humidityChange -= 1.0 * changeRateMultiplier;
                foreach (var fan in Devices.OfType<FanDevice>().Where(d => d.CurrentState == DeviceState.Working && d.IsHumidityControlled)) humidityChange -= 4.0 * changeRateMultiplier;
                CurrentHumidity = Math.Max(0, Math.Min(100, CurrentHumidity + (int)Math.Round(humidityChange)));
            }
        }

        public void ResetToDefault()
        {
            CurrentTemperature = DefaultTemperature; CurrentHumidity = DefaultHumidity;
            HasFire = false; HasMotion = false; IsBreached = false; MotionEndTime = TimeSpan.Zero;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class DeviceBorderColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { if (value is DeviceState state) { switch (state) { case DeviceState.Working: case DeviceState.Active: return Brushes.Green; case DeviceState.NotWorking: case DeviceState.Destroyed: case DeviceState.EmptyWater: return Brushes.Black; case DeviceState.Off: case DeviceState.Inactive: return Brushes.Gray; case DeviceState.NeedsRecharge: return Brushes.OrangeRed; case DeviceState.Charging: return Brushes.BlueViolet; default: return Brushes.DarkGray; } } return Brushes.DarkGray; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DeviceFillColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { if (value is DeviceType type) { switch (type) { case DeviceType.Window: return Brushes.Cyan; case DeviceType.Lamp: return Brushes.Yellow; case DeviceType.Camera: return Brushes.Violet; case DeviceType.FireSystem: return Brushes.DarkBlue; case DeviceType.Thermostat: return Brushes.Red; case DeviceType.Heater: return Brushes.Green; case DeviceType.Conditioner: return Brushes.Turquoise; case DeviceType.Humidifier: return Brushes.LightBlue; case DeviceType.Dehumidifier: return Brushes.DarkGoldenrod; case DeviceType.Chandelier: return Brushes.Orange; case DeviceType.Fan: return Brushes.Blue; case DeviceType.Special: return Brushes.Pink; default: return Brushes.LightGray; } } return Brushes.LightGray; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToButtonContentConverter : IValueConverter
    {
        public string TrueValue { get; set; } = "True Action"; public string FalseValue { get; set; } = "False Action";
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value is bool b && b ? TrueValue : FalseValue; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { return value is bool b && b ? Visibility.Visible : Visibility.Collapsed; }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    public class InvertedBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { return value is bool b && b ? Visibility.Collapsed : Visibility.Visible; }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    public class DeviceStateToActionTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { if (!(value is DeviceState state) || !(parameter is string actionNumber)) return null; if (actionNumber == "Action1") { switch (state) { case DeviceState.Off: case DeviceState.Inactive: return "Увімк."; case DeviceState.Working: case DeviceState.Active: return "Вимк."; case DeviceState.Destroyed: return "Полаг."; case DeviceState.EmptyWater: case DeviceState.NotWorking: return "Перезар."; case DeviceState.NeedsRecharge: return "Зарядка..."; case DeviceState.Charging: return "Зарядка..."; } } return null; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DeviceActionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { var actionText = new DeviceStateToActionTextConverter().Convert(value, typeof(string), parameter, culture); return actionText != null ? Visibility.Visible : Visibility.Collapsed; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute; private readonly Func<bool> _canExecute;
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public RelayCommand(Action execute, Func<bool> canExecute = null) { _execute = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;

            T correctlyTypedParam = default(T); // Ініціалізуємо значенням за замовчуванням

            if (parameter == null)
            {
                // Якщо T - тип значення і не Nullable, null не може бути присвоєний.
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    return false;
                // Для посилальних типів та Nullable<TValue>, default(T) буде null.
            }
            else if (parameter is T tempParam)
            {
                correctlyTypedParam = tempParam;
            }
            else
            {
                return false; // Параметр неправильного типу
            }
            return _canExecute(correctlyTypedParam);
        }

        public void Execute(object parameter)
        {
            T correctlyTypedParam = default(T); // Ініціалізуємо

            if (parameter == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    return; // Не виконуємо для не-nullable типів значень з null
            }
            else if (parameter is T tempParam)
            {
                correctlyTypedParam = tempParam;
            }
            else
            {
                return; // Параметр неправильного типу, не виконуємо
            }
            _execute(correctlyTypedParam);
        }
    }
}