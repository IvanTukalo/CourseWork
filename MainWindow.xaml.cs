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
        // Simulation scaling: with UPDATE_INTERVAL_SECONDS = 0.5s and SIMULATION_SPEED_FACTOR = 0.1,
        // each tick advances 0.5s * 60 * 0.1 = 3 simulated minutes => 6 simulated minutes per real second.
        public const double SIMULATION_SPEED_FACTOR = 0.1;
        public const double UPDATE_INTERVAL_SECONDS = 0.5;

        private DispatcherTimer _simulationTimer;
        private DispatcherTimer _alarmTimer;
        private TimeSpan _alarmEndTime = TimeSpan.Zero;
        private bool _isAlarmPlaying = false;

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
            set 
            { 
                _isElectricityOn = value; 
                OnPropertyChanged(); 
                if (value)
                    LogEvent("Постачання електроенергії відновлено");
                else
                    LogEvent("Відсутнє постачання електроенергії");
                UpdateAllDeviceStatesAfterPowerChange(); 
            }
        }

        // New: Pause/Resume simulation state
        private bool _isSimulationPaused = false;
        public bool IsSimulationPaused
        {
            get => _isSimulationPaused;
            set { if (_isSimulationPaused != value) { _isSimulationPaused = value; OnPropertyChanged(); } }
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
            
            _alarmTimer = new DispatcherTimer();
            _alarmTimer.Interval = TimeSpan.FromMilliseconds(500);
            _alarmTimer.Tick += AlarmTimer_Tick;
        }

        private void AlarmTimer_Tick(object sender, EventArgs e)
        {
            if (CurrentSimTime >= _alarmEndTime)
            {
                StopAlarm();
            }
            else
            {
                // Play beep sound (Windows system beep)
                System.Media.SystemSounds.Exclamation.Play();
            }
        }

        public void StartAlarm()
        {
            if (!_isAlarmPlaying)
            {
                _isAlarmPlaying = true;
                _alarmEndTime = CurrentSimTime.Add(TimeSpan.FromMinutes(30));
                _alarmTimer.Start();
            }
        }

        private void StopAlarm()
        {
            _isAlarmPlaying = false;
            _alarmTimer.Stop();
        }

        private void InitializeDevicesAndRooms()
        {
            _allDevices = new List<SmartDeviceBase>();
            _allRooms = new List<Room>();

            // Створення кімнат на основі розмірів зображення плану (1446x1688) з урахуванням відступів
            // Це забезпечує збереження пропорцій та відповідності описаному макету:
            // Row 1: "Вітальня" зліва (прибл. 1/3 висоти, 3/5 ширини), "Спальня 2" справа (залишок ширини, майже 1/2 висоти)
            // Row 2: "Кухня" (квадрат) зліва під "Вітальня", правіше "Хол"; під "Спальня 2" розміщена "Спальня 1" (трохи менша за "Спальня 2")
            // Row 3: "Ванна" зліва під "Кухня", "Тамбур" під "Хол", праворуч/внизу продовження зони для "Спальня 1"
            // "Крильце" внизу по центру, вирівняне по центру з "Тамбур".
            double IMG_W = 1446, IMG_H = 1688;
            double padX = 0.145 * IMG_W;         // бічні відступи від країв зображення
            double padTop = 0.137 * IMG_H;       // верхній відступ
            double padBottom = 0.15 * IMG_H;    // нижній відступ
            double innerW = IMG_W - 2 * padX;
            double innerH = IMG_H - padTop - padBottom;
            double gapX = 0.0075 * IMG_W;
            double gapY = 0.007 * IMG_H;

            double leftW = 0.575 * innerW;
            double rightW = innerW - leftW;

            // Row 1
            double row1H = 0.335 * innerH;
            Rect livingRoomRect = new Rect(padX, padTop, leftW, row1H);
            Rect bedroom2Rect = new Rect(padX + leftW + gapX, padTop, 0.98 * Math.Max(0, rightW - gapX), 0.458 * innerH);

            // Row 2 (під "Вітальня"): Кухня (квадрат) + Хол (праворуч)
            double row2Top = padTop + row1H + gapY;
            double kitchenSize = leftW * 0.59;
            double kitchenH = kitchenSize * 0.905;
            Rect kitchenRect = new Rect(padX, row2Top, kitchenSize, kitchenH);
            Rect hallRect = new Rect(kitchenRect.X + kitchenRect.Width + gapX, row2Top,
                                      Math.Max(0, leftW - kitchenRect.Width - gapX), kitchenSize * 1.265);

            // Row 3 (зліва під Кухня та під Хол): Ванна та Тамбур
            double row3Top = kitchenRect.Y + kitchenRect.Height + gapY;
            double bathroomH = kitchenH * 0.88;
            Rect bathroomRect = new Rect(padX, row3Top, kitchenRect.Width, bathroomH);
            Rect tambourRect = new Rect(hallRect.X, hallRect.Y + hallRect.Height + gapY,
                                        hallRect.Width, kitchenH + bathroomH - hallRect.Height);

            // Права колонка знизу: Спальня 1 (трохи менша за Спальня 2)
            double s1Top = padTop + bedroom2Rect.Height + gapY;
            double s1H = 0.38 * innerH;
            Rect bedroom1Rect = new Rect(padX + leftW + gapX, s1Top, 0.98 * Math.Max(0, rightW - gapX), s1H);

            // Крильце: по центру внизу, вирівняне по центру з Тамбур
            double porchW = tambourRect.Width * 4.415;
            double porchH = tambourRect.Height * 1.3;
            double porchX = tambourRect.X + tambourRect.Width / 1.55 - porchW / 2;
            double porchY = tambourRect.Y + tambourRect.Height;
            Rect porchRect = new Rect(porchX, porchY, porchW, porchH);

            var livingRoom = new Room("Вітальня", livingRoomRect, this) { DefaultTemperature = 23, DefaultHumidity = 45 };
            var bedroom2 = new Room("Спальня 2", bedroom2Rect, this) { DefaultTemperature = 22, DefaultHumidity = 50 };
            var kitchen = new Room("Кухня", kitchenRect, this) { DefaultTemperature = 21, DefaultHumidity = 55 };
            var hall = new Room("Хол", hallRect, this) { DefaultTemperature = 22, DefaultHumidity = 45 };
            var bathroom = new Room("Ванна", bathroomRect, this) { DefaultTemperature = 24, DefaultHumidity = 60 };
            var tambour = new Room("Тамбур", tambourRect, this) { DefaultTemperature = 20, DefaultHumidity = 50 };
            var bedroom1 = new Room("Спальня 1", bedroom1Rect, this) { DefaultTemperature = 22, DefaultHumidity = 50 };
            var porch = new Room("Крильце", porchRect, this) { DefaultTemperature = 18, DefaultHumidity = 50 };
            
            _allRooms.AddRange(new[] { livingRoom, bedroom2, kitchen, hall, bathroom, tambour, bedroom1, porch });

            foreach (var room in _allRooms)
            {
                RoomAreas.Add(new RoomAreaViewModel(room.Name, room.AreaRect));
            }

            // Створення пристроїв (кожен пристрій отримує посилання на MainWindow)
            // Windows (В)
            const int windowDeviceWidth = 170, windowDeviceHeight = 33; // already x3
            // Common size constants for other devices (3x original values)
            const int motionLampSize = 45;
            const int cameraSize = 48;
            const int fireSprinklerSize = 41;
            const int thermostatWidth = 30, thermostatHeight = 45;
            const int heaterWidth = 38, heaterHeight = 96;
            const int conditionerWidth = 101, conditionerHeight = 38;
            const int humidifierWidth = 47, humidifierHeight = 63;
            const int dehumidifierWidth = 47, dehumidifierHeight = 63;
            const int chandelierSize = 57;
            const int fanSize = 53;
            const int solarPanelWidth = 278, solarPanelHeight = 90;
            const int sirenWidth = 45, sirenHeight = 60;
            const int doorWidth = 100, doorHeight = 27;
            const int batteryWidth = 55, batteryHeight = 104;
            const int manualSwitchSize = 30;
            const int stoveWidth = 65, stoveHeight = 106;

            _allDevices.Add(new WindowDevice("В1", "W001", bathroom, new Point(110, 248), new Size(windowDeviceWidth * 0.70, windowDeviceHeight), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В2", "W002", bedroom1, new Point(158, 427), new Size(windowDeviceWidth * 0.70, windowDeviceHeight), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В3", "W003", bedroom1, new Point(386, 140), new Size(windowDeviceHeight, windowDeviceWidth), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В4", "W004", bedroom2, new Point(387, 263), new Size(windowDeviceHeight, windowDeviceWidth), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В5", "W005", bedroom2, new Point(118, 0), new Size(windowDeviceWidth, windowDeviceHeight), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В6", "W006", livingRoom, new Point(375, 0), new Size(windowDeviceWidth, windowDeviceHeight), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В7", "W007", livingRoom, new Point(116, 0), new Size(windowDeviceWidth, windowDeviceHeight), this) { DeviceType = DeviceType.Window });
            _allDevices.Add(new WindowDevice("В8", "W008", kitchen, new Point(-2, 63), new Size(windowDeviceHeight, windowDeviceWidth), this) { DeviceType = DeviceType.Window });


            _allDevices.Add(new MotionSensorLamp("Л1", "L001", porch, new Point(377, -10), new Size(motionLampSize, motionLampSize), this) { DeviceType = DeviceType.Lamp, ActivationDurationInSimMinutes = 12 });
            _allDevices.Add(new MotionSensorLamp("Л2", "L002", tambour, new Point(185, 54), new Size(motionLampSize, motionLampSize), this) { DeviceType = DeviceType.Lamp, ActivationDurationInSimMinutes = 12 });


            _allDevices.Add(new CameraDevice("К1", "C001", porch, new Point(922, 0), new Size(cameraSize, cameraSize), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К2", "C002", hall, new Point(3, 21), new Size(cameraSize, cameraSize), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К3", "C003", bedroom1, new Point(18, 0), new Size(cameraSize, cameraSize), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К4", "C004", bedroom2, new Point(17, 500), new Size(cameraSize, cameraSize), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });
            _allDevices.Add(new CameraDevice("К5", "C005", livingRoom, new Point(540, 330), new Size(cameraSize, cameraSize), this) { DeviceType = DeviceType.Camera, ActivationDurationInSimMinutes = 30 });

            _allDevices.Add(new FireSprinklerDevice("ВП1", "FS001", tambour, new Point(96, 35), new Size(fireSprinklerSize, fireSprinklerSize), 0.80, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП2", "FS002", hall, new Point(99, 335), new Size(fireSprinklerSize, fireSprinklerSize), 0.60, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП3", "FS003", hall, new Point(97, 83), new Size(fireSprinklerSize, fireSprinklerSize), 0.75, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП4", "FS004", bedroom1, new Point(191, 319), new Size(fireSprinklerSize, fireSprinklerSize), 0.50, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП5", "FS005", bedroom1, new Point(189, 117), new Size(fireSprinklerSize, fireSprinklerSize), 0.90, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП6", "FS006", bedroom2, new Point(177, 402), new Size(fireSprinklerSize, fireSprinklerSize), 0.60, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП7", "FS007", bedroom2, new Point(182, 141), new Size(fireSprinklerSize, fireSprinklerSize), 0.85, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП8", "FS008", kitchen, new Point(141, 155), new Size(fireSprinklerSize, fireSprinklerSize), 1.00, this, true) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП9", "FS009", livingRoom, new Point(124, 189), new Size(fireSprinklerSize, fireSprinklerSize), 0.90, this) { DeviceType = DeviceType.FireSystem });
            _allDevices.Add(new FireSprinklerDevice("ВП10", "FS010", livingRoom, new Point(410, 193), new Size(fireSprinklerSize, fireSprinklerSize), 0.95, this) { DeviceType = DeviceType.FireSystem });

            _allDevices.Add(new ThermostatDevice("Т1", "TH001", bedroom1, new Point(0, 175), new Size(thermostatWidth, thermostatHeight), this) { DeviceType = DeviceType.Thermostat });
            _allDevices.Add(new ThermostatDevice("Т2", "TH002", bedroom2, new Point(0, 373), new Size(thermostatWidth, thermostatHeight), this) { DeviceType = DeviceType.Thermostat });
            _allDevices.Add(new ThermostatDevice("Т3", "TH003", livingRoom, new Point(330, 373), new Size(thermostatHeight, thermostatWidth), this) { DeviceType = DeviceType.Thermostat });

            _allDevices.Add(new HeaterDevice("О1", "H001", bedroom1, new Point(0, 220), new Size(heaterWidth, heaterHeight), this) { DeviceType = DeviceType.Heater });
            _allDevices.Add(new HeaterDevice("О2", "H002", bedroom2, new Point(87, 512), new Size(heaterHeight, heaterWidth), this) { DeviceType = DeviceType.Heater });
            _allDevices.Add(new HeaterDevice("О3", "H003", livingRoom, new Point(23, 227), new Size(heaterWidth, heaterHeight), this) { DeviceType = DeviceType.Heater });

            _allDevices.Add(new ConditionerDevice("КОН1", "AC001", bedroom1, new Point(21, 394), new Size(conditionerWidth, conditionerHeight), this) { DeviceType = DeviceType.Conditioner });
            _allDevices.Add(new ConditionerDevice("КОН2", "AC002", bedroom2, new Point(194, 512), new Size(conditionerWidth, conditionerHeight), this) { DeviceType = DeviceType.Conditioner });
            _allDevices.Add(new ConditionerDevice("КОН3", "AC003", livingRoom, new Point(551, 107), new Size(conditionerHeight, conditionerWidth), this) { DeviceType = DeviceType.Conditioner });

            _allDevices.Add(new HumidifierDevice("З1", "HU001", bedroom1, new Point(250, 370), new Size(humidifierWidth, humidifierHeight), this) { DeviceType = DeviceType.Humidifier });
            _allDevices.Add(new HumidifierDevice("З2", "HU002", bedroom2, new Point(-2, 285), new Size(humidifierWidth, humidifierHeight), this) { DeviceType = DeviceType.Humidifier });
            _allDevices.Add(new HumidifierDevice("З3", "HU003", livingRoom, new Point(544, 7), new Size(humidifierWidth, humidifierHeight), this) { DeviceType = DeviceType.Humidifier });

            _allDevices.Add(new DehumidifierDevice("ОС1", "DH001", bedroom1, new Point(143, 370), new Size(dehumidifierWidth, dehumidifierHeight), this) { DeviceType = DeviceType.Dehumidifier });
            _allDevices.Add(new DehumidifierDevice("ОС2", "DH002", bedroom2, new Point(-2, 223), new Size(dehumidifierWidth, dehumidifierHeight), this) { DeviceType = DeviceType.Dehumidifier });
            _allDevices.Add(new DehumidifierDevice("ОС3", "DH003", livingRoom, new Point(544, 220), new Size(dehumidifierWidth, dehumidifierHeight), this) { DeviceType = DeviceType.Dehumidifier });

            _allDevices.Add(new ChandelierDevice("ЛЮ1", "CH001", hall, new Point(85, 152), new Size(chandelierSize, chandelierSize), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ2", "CH002", bedroom1, new Point(180, 202), new Size(chandelierSize, chandelierSize), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ3", "CH003", bedroom2, new Point(165, 258), new Size(chandelierSize, chandelierSize), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ4", "CH004", livingRoom, new Point(259, 187), new Size(chandelierSize, chandelierSize), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ5", "CH005", kitchen, new Point(169, 102), new Size(chandelierSize, chandelierSize), this) { DeviceType = DeviceType.Chandelier });
            _allDevices.Add(new ChandelierDevice("ЛЮ6", "CH006", bathroom, new Point(240, 88), new Size(chandelierSize, chandelierSize), this) { DeviceType = DeviceType.Chandelier });

            _allDevices.Add(new FanDevice("ВЕ1", "F001", bathroom, new Point(24, 109), new Size(fanSize, fanSize), this, true) { DeviceType = DeviceType.Fan });
            _allDevices.Add(new FanDevice("ВЕ2", "F002", kitchen, new Point(31, 79), new Size(fanSize, fanSize), this, false) { DeviceType = DeviceType.Fan });

            _allDevices.Add(new SolarPanelDevice("СП1", "SP001", porch, new Point(0, 92), new Size(solarPanelWidth, solarPanelHeight), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new SolarPanelDevice("СП2", "SP002", porch, new Point(680, 92), new Size(solarPanelWidth, solarPanelHeight), this) { DeviceType = DeviceType.Special }); // Added СП2
            _allDevices.Add(new SirenDevice("С1", "SR001", hall, new Point(4, 247), new Size(sirenWidth, sirenHeight), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new DoorDevice("Д1", "D001", porch, new Point(419, -27), new Size(doorWidth, doorHeight), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new BatteryDevice("Б1", "B001", hall, new Point(175, 335), new Size(batteryWidth, batteryHeight), this) { DeviceType = DeviceType.Special });
            _allDevices.Add(new ManualSwitchDevice("Перемикач ВП8", "SW001", kitchen, new Point(318, 50), new Size(manualSwitchSize, manualSwitchSize), this,
                (simTime, powerOn) => {
                    var vp8 = _allDevices.OfType<FireSprinklerDevice>().FirstOrDefault(d => d.Id == "FS008");
                    vp8?.ActivateManual(simTime); // Pass current simTime
                    // LogEvent is called from within ActivateManual now
                })
            { DeviceType = DeviceType.Special });
            _allDevices.Add(new StoveDevice("Плита", "ST001", kitchen, new Point(30, 133), new Size(stoveWidth, stoveHeight), this) { DeviceType = DeviceType.Special });

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
            if (!IsSimulationPaused)
            {
                _simulationTimer.Start();
                LogEvent("Симуляцію розпочато.");
            }
            else
            {
                LogEvent("Симуляцію призупинено.");
            }
        }

        private void SimulationTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan elapsedSimTimePerTick = TimeSpan.FromMinutes(UPDATE_INTERVAL_SECONDS * 60 * SIMULATION_SPEED_FACTOR);
            CurrentSimTime += elapsedSimTimePerTick;

            bool hasSolarPower = _allDevices.OfType<SolarPanelDevice>().Any(p => p.CurrentState == DeviceState.Working);
            var mainBattery = _allDevices.OfType<BatteryDevice>().FirstOrDefault();
            bool batteryHasPower = mainBattery != null && mainBattery.ChargeLevel > 0;
            bool effectivePower = IsElectricityOn || hasSolarPower || (!IsElectricityOn && batteryHasPower);

            // Визначаємо, чи батарея має живити критичні пристрої
            bool batteryPoweringDevices = !IsElectricityOn && !hasSolarPower && batteryHasPower;

            // SET BATTERY STATE BEFORE UPDATING DEVICES
            // Підраховуємо кількість активних критичних пристроїв, що живляться від батареї
            int activeCriticalDevices = 0;
            if (batteryPoweringDevices)
            {
                // Камери К1-К5
                activeCriticalDevices += _allDevices.OfType<CameraDevice>().Count(d => d.CurrentState != DeviceState.Off);
                // Спринклери ВП1-ВП10
                activeCriticalDevices += _allDevices.OfType<FireSprinklerDevice>().Count(d => d.CurrentState == DeviceState.Working || d.CurrentState == DeviceState.Active);
                // Сирена С1
                activeCriticalDevices += _allDevices.OfType<SirenDevice>().Count(d => d.CurrentState == DeviceState.Active);
                // Вікна В1-В8 (сенсори)
                activeCriticalDevices += _allDevices.OfType<WindowDevice>().Count(d => d.CurrentState == DeviceState.Working);
                // Двері Д1 (сенсори)
                activeCriticalDevices += _allDevices.OfType<DoorDevice>().Count(d => d.CurrentState == DeviceState.Working);
                // Термостати Т1-Т3 (сенсори)
                activeCriticalDevices += _allDevices.OfType<ThermostatDevice>().Count(d => d.CurrentState == DeviceState.Working);
                // Вентилятори ВЕ1-ВЕ2
                activeCriticalDevices += _allDevices.OfType<FanDevice>().Count(d => d.CurrentState == DeviceState.Working);
            }

            // Set battery state BEFORE updating all devices
            if (mainBattery != null)
            {
                mainBattery.IsDischarging = batteryPoweringDevices;
                mainBattery.IsCharging = (IsElectricityOn || hasSolarPower) && mainBattery.ChargeLevel < 100.0;
                mainBattery.ActiveDeviceCount = activeCriticalDevices;
            }

            foreach (var room in _allRooms)
                room.UpdateEnvironment(elapsedSimTimePerTick, effectivePower);

            foreach (var device in _allDevices)
            {
                // Pass grid power to BatteryDevice to allow correct charge/discharge decisions
                bool powerFlag = (device is BatteryDevice) ? IsElectricityOn : effectivePower;
                device.UpdateState(CurrentSimTime, powerFlag, device.AssociatedRoom);
                if (device is StoveDevice stove && stove.CurrentState == DeviceState.Working)
                    _allDevices.OfType<FanDevice>().FirstOrDefault(f => f.Id == "F002")?.ActivateManual(effectivePower);
            }

            SelectedRoomInfo?.Refresh();
        }
        private void UpdateAllDeviceStatesAfterPowerChange()
        {
            bool hasSolarPower = _allDevices.OfType<SolarPanelDevice>().Any(p => p.CurrentState == DeviceState.Working);
            var mainBattery = _allDevices.OfType<BatteryDevice>().FirstOrDefault();
            bool batteryHasPower = mainBattery != null && mainBattery.ChargeLevel > 0;
            bool batteryPoweringDevices = !IsElectricityOn && !hasSolarPower && batteryHasPower;

            // Recalculate active critical devices when toggling power
            int activeCriticalDevices = 0;
            if (batteryPoweringDevices)
            {
                activeCriticalDevices += _allDevices.OfType<CameraDevice>().Count(d => d.CurrentState != DeviceState.Off);
                activeCriticalDevices += _allDevices.OfType<FireSprinklerDevice>().Count(d => d.CurrentState == DeviceState.Working || d.CurrentState == DeviceState.Active);
                activeCriticalDevices += _allDevices.OfType<SirenDevice>().Count(d => d.CurrentState == DeviceState.Active);
                activeCriticalDevices += _allDevices.OfType<WindowDevice>().Count(d => d.CurrentState == DeviceState.Working);
                activeCriticalDevices += _allDevices.OfType<DoorDevice>().Count(d => d.CurrentState == DeviceState.Working);
                activeCriticalDevices += _allDevices.OfType<ThermostatDevice>().Count(d => d.CurrentState == DeviceState.Working);
                activeCriticalDevices += _allDevices.OfType<FanDevice>().Count(d => d.CurrentState == DeviceState.Working);
            }

            if (mainBattery != null)
            {
                mainBattery.IsDischarging = batteryPoweringDevices;
                mainBattery.IsCharging = (IsElectricityOn || hasSolarPower) && mainBattery.ChargeLevel < 100.0;
                mainBattery.ActiveDeviceCount = activeCriticalDevices;
            }

            bool effectivePower = IsElectricityOn || hasSolarPower || (!IsElectricityOn && batteryHasPower);

            foreach (var device in _allDevices)
            {
                bool powerFlag = (device is BatteryDevice) ? IsElectricityOn : effectivePower;
                device.UpdateState(CurrentSimTime, powerFlag, device.AssociatedRoom);
            }
            SelectedRoomInfo?.Refresh();
        }


        private void ChangeTime_Click(object sender, RoutedEventArgs e)
        {
            var newTimeStr = ShowInputDialog("Введіть новий час (ГГ:ХХ або ГГ ХХ):", "Зміна часу");

            if (!string.IsNullOrWhiteSpace(newTimeStr))
            {
                // Normalize input: allow either colon or space between hours and minutes
                string normalized = newTimeStr.Trim();
                normalized = Regex.Replace(normalized, "\\s+", ":"); // replace any whitespace with a colon

                if (TimeSpan.TryParse(normalized, out TimeSpan newTime))
                {
                    CurrentSimTime = newTime;
                    LogEvent($"Час змінено на {newTime:hh\\:mm}");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(newTimeStr))
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
            if (!IsSimulationPaused)
            {
                LogEvent("Симуляцію розпочато.");
                _simulationTimer.Start();
            }
            else
            {
                LogEvent("Симуляцію призупинено.");
            }
        }

        // New: Pause/Resume button click handler
        private void TogglePause_Click(object sender, RoutedEventArgs e)
        {
            if (IsSimulationPaused)
            {
                IsSimulationPaused = false;
                _simulationTimer.Start();
                LogEvent("Симуляцію відновлено.");
            }
            else
            {
                IsSimulationPaused = true;
                _simulationTimer.Stop();
                LogEvent("Симуляцію призупинено.");
            }
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
                if (int.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out int temp))
                    SelectedRoomInfo.Temperature = Math.Max(-273, Math.Min(1000, temp));
                tb.Text = SelectedRoomInfo.Temperature.ToString("F0", CultureInfo.InvariantCulture);
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
        // New: absolute position on canvas = room's top-left + device's relative position
        public Point AbsolutePosition => Device.AssociatedRoom != null
            ? new Point(Device.AssociatedRoom.AreaRect.X + Device.Position.X,
                        Device.AssociatedRoom.AreaRect.Y + Device.Position.Y)
            : Device.Position;
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
                if (e.PropertyName == nameof(SmartDeviceBase.Position) || e.PropertyName == nameof(SmartDeviceBase.AssociatedRoom))
                { OnPropertyChanged(nameof(Position)); OnPropertyChanged(nameof(AbsolutePosition)); }
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
        public void Highlight(bool highlight) { BorderBrush = highlight ? Brushes.Red : Brushes.Transparent; BorderThickness = highlight ? 3 : 0; }
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
        public int Temperature { get => _currentRoom?.CurrentTemperature ?? 0; set { if (_currentRoom != null) { _currentRoom.CurrentTemperature = value; OnPropertyChanged(); OnPropertyChanged(nameof(TemperatureString)); } } }
        public string TemperatureString { get => Temperature.ToString("F0", CultureInfo.InvariantCulture); set { if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out int temp)) Temperature = Math.Max(-273, temp); OnPropertyChanged(); } }
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
            IncreaseTempCommand = new RelayCommand(() => Temperature = Math.Min(1000, Temperature + 1));
            DecreaseTempCommand = new RelayCommand(() => Temperature = Math.Max(-273, Temperature - 1));
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
        public DeviceState CurrentState 
        { 
            get => _currentState; 
            protected set 
            { 
                if (_currentState != value) 
                { 
                    var oldState = _currentState;
                    _currentState = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(CurrentStateDescription));
                    
                    // Log status change with proper Ukrainian description
                    string statusName = GetStatusName(value);
                    if (!string.IsNullOrEmpty(statusName))
                    {
                        MainWindowContext?.LogEvent($"Об'єкт {Name} змінив свій статус на {statusName}");
                    }
                } 
            } 
        }
        
        protected virtual string GetStatusName(DeviceState state)
        {
            // Default status names - can be overridden in derived classes
            switch (state)
            {
                case DeviceState.Off: return "Вимкнено";
                case DeviceState.Working: return "Працює";
                case DeviceState.NotWorking: return "Не працює";
                case DeviceState.Active: return "Активний";
                case DeviceState.Inactive: return "Неактивний";
                case DeviceState.Destroyed: return "Зруйновано";
                case DeviceState.EmptyWater: return "Порожній";
                case DeviceState.NeedsRecharge: return "Перезарядка";
                case DeviceState.Charging: return "Зарядка";
                default: return state.ToString();
            }
        }
        
        public virtual string CurrentStateDescription => CurrentState.ToString();
        protected SmartDeviceBase(string name, string id, Room room, Point position, Size size, MainWindow mainWindowContext)
        { Name = name; Id = id; AssociatedRoom = room; Position = position; Size = size; CurrentState = DeviceState.Off; MainWindowContext = mainWindowContext; }
        public abstract void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment);
        public virtual void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime) { }
        public virtual void Reset() { CurrentState = DeviceState.Off; }
        public virtual void Repair() { if (CurrentState == DeviceState.Destroyed || CurrentState == DeviceState.NotWorking) { CurrentState = DeviceState.Working; } }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected void Log(string message)
        {
            string roomInfo = AssociatedRoom != null && !string.IsNullOrEmpty(AssociatedRoom.Name)
                ? $"(Room: {AssociatedRoom.Name})"
                : "(Room: Unknown)";
            MainWindowContext?.LogEvent($"[{Name}] {roomInfo} {message}");
        }
    }

    public interface IOnOffToggleable { void TurnOn(bool isElectricityOn); void TurnOff(); void ToggleState(bool isElectricityOn); }

    public class WindowDevice : SmartDeviceBase
    {
        public WindowDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Working: return "Ціле";
                case DeviceState.Off: return "Вимкнено(зруйноване)";
                case DeviceState.Destroyed: return "Зруйноване";
                default: return base.GetStatusName(state);
            }
        }
        
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
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Working: return "Ціле";
                case DeviceState.Off: return "Вимкнено(зруйноване)";
                case DeviceState.Destroyed: return "Зруйноване";
                default: return base.GetStatusName(state);
            }
        }
        
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
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Active: return "Активна(датчик руху)";
                case DeviceState.Working: return "Працює(вручну)";
                case DeviceState.Off: return "Вимкнена";
                default: return base.GetStatusName(state);
            }
        }
        
        public override string CurrentStateDescription { get { if (CurrentState == DeviceState.Active) return "Активна (датчик)"; if (CurrentState == DeviceState.Working) return "Працює (вручну)"; if (CurrentState == DeviceState.Off) return "Вимкнена"; return CurrentState.ToString(); } }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            if (!isElectricityOn && CurrentState != DeviceState.Off) { TurnOff(); return; }
            bool canWorkByTime = (Name == "Л1" && (currentTime.Hours >= 16 || currentTime.Hours < 8)) || Name != "Л1";
            if (environment?.HasMotion == true && canWorkByTime && isElectricityOn)
            { if (CurrentState != DeviceState.Active && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Active; } _activeUntil = currentTime.Add(TimeSpan.FromMinutes(ActivationDurationInSimMinutes)); environment.HasMotion = false; }
            if (CurrentState == DeviceState.Active && currentTime > _activeUntil && !_isManuallyOn) TurnOff();
            if (!canWorkByTime && Name == "Л1" && CurrentState != DeviceState.Off) TurnOff();
        }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn) { CurrentState = DeviceState.Working; _isManuallyOn = true; } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; _isManuallyOn = false; } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
        public override void Reset() { base.Reset(); _activeUntil = TimeSpan.Zero; _isManuallyOn = false; }
    }

    public class CameraDevice : SmartDeviceBase
    {
        public int ActivationDurationInSimMinutes { get; set; } = 30;
        private TimeSpan _activeUntil;
        public CameraDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Active: return "Запис(датчик руху)";
                case DeviceState.Working: return "Працює";
                case DeviceState.Off: return "Вимкнена";
                default: return base.GetStatusName(state);
            }
        }
        
        public override string CurrentStateDescription => CurrentState == DeviceState.Active ? "Запис (датчик)" : (CurrentState == DeviceState.Working ? "Працює" : "Вимкнена");
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            // Камера працює від батареї при відсутності електроенергії
            // Перевіряємо чи є живлення (електрика або батарея з зарядом)
            if (!isElectricityOn && CurrentState != DeviceState.Off)
            {
                // Вимикаємо камеру - вона буде увімкнена знову якщо є батарея
                CurrentState = DeviceState.Off;
                return;
            }
            
            // Якщо є живлення, камера працює
            if (isElectricityOn || CurrentState == DeviceState.Working)
            {
                if (environment?.HasMotion == true)
                { 
                    if (CurrentState != DeviceState.Active) 
                    { 
                        CurrentState = DeviceState.Active; 
                    } 
                    _activeUntil = currentTime.Add(TimeSpan.FromMinutes(ActivationDurationInSimMinutes)); 
                    environment.HasMotion = false; 
                }
                
                if (CurrentState == DeviceState.Active && currentTime > _activeUntil) 
                { 
                    CurrentState = DeviceState.Working; 
                }
                
                // Якщо камера була вимкнена але є живлення, вмикаємо
                if (CurrentState == DeviceState.Off && isElectricityOn)
                {
                    CurrentState = DeviceState.Working;
                }
            }
        }
        public override void Reset() { base.Reset(); _activeUntil = TimeSpan.Zero; CurrentState = DeviceState.Working; }
    }

    public class FireSprinklerDevice : SmartDeviceBase
    {
        private static readonly Random _randomInternalStatic = new Random();
        public double ActivationProbability { get; }
        public bool IsManuallyActivatable { get; }
        private readonly TimeSpan _activeDurationSim = TimeSpan.FromMinutes(6);
        private TimeSpan _activeUntil; 
        private TimeSpan _rechargeCompleteTime;
        private TimeSpan _emptyWaterTime = TimeSpan.Zero;
        private const int RECHARGE_DURATION_SIM_MINUTES = 60;
        private const int AUTO_RECHARGE_START_DELAY_MINUTES = 30;
        public FireSprinklerDevice(string name, string id, Room room, Point position, Size size, double probability, MainWindow mw, bool manual = false)
            : base(name, id, room, position, size, mw) { ActivationProbability = probability; IsManuallyActivatable = manual; CurrentState = DeviceState.Working; }
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Working: return "Готова(повна)";
                case DeviceState.Active: return "Гасіння пожежі";
                case DeviceState.EmptyWater: return "Пуста(немає води)";
                case DeviceState.NeedsRecharge: return "Перезарядка";
                case DeviceState.Off: return IsManuallyActivatable ? "Вимкнено(вручну)" : "Вимкнено";
                default: return base.GetStatusName(state);
            }
        }
        
        public override string CurrentStateDescription { get { switch (CurrentState) { case DeviceState.Working: return "Готова (повна)"; case DeviceState.Active: return "Гасіння пожежі"; case DeviceState.EmptyWater: return "Пуста (немає води)"; case DeviceState.NeedsRecharge: return "Перезарядка..."; case DeviceState.Off: return IsManuallyActivatable ? "Вимкнено (Вручну)" : "Вимкнено"; default: return CurrentState.ToString(); } } }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            if (CurrentState == DeviceState.NeedsRecharge && currentTime >= _rechargeCompleteTime) Recharge();
            
            if (CurrentState == DeviceState.EmptyWater && currentTime >= _emptyWaterTime.Add(TimeSpan.FromMinutes(AUTO_RECHARGE_START_DELAY_MINUTES)))
            {
                StartRechargeCycle(currentTime);
            }
            
            if (CurrentState == DeviceState.Active && currentTime >= _activeUntil)
            { 
                CurrentState = DeviceState.EmptyWater; 
                _emptyWaterTime = currentTime;
                if (environment?.HasFire == true) 
                { 
                    MainWindowContext?.LogEvent($"Пожежа в приміщенні {environment.Name} непогашена, виклик Пожежників");
                    MainWindowContext?.StartAlarm();
                } 
            }
            if (CurrentState == DeviceState.Working && environment?.HasFire == true && !IsManuallyActivatable && isElectricityOn && _randomInternalStatic.NextDouble() < ActivationProbability) Activate(currentTime, environment);
        }
        private void Activate(TimeSpan currentTime, Room environment) { CurrentState = DeviceState.Active; _activeUntil = currentTime + _activeDurationSim; if (environment != null) environment.HasFire = false; }
        public void ActivateManual(TimeSpan currentTime) { if (CurrentState == DeviceState.Working) { CurrentState = DeviceState.Active; _activeUntil = currentTime + _activeDurationSim; if (AssociatedRoom != null) AssociatedRoom.HasFire = false; } }
        public void Recharge() { if (CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; _emptyWaterTime = TimeSpan.Zero; } }
        public void StartRechargeCycle(TimeSpan currentTime) { if (CurrentState == DeviceState.EmptyWater || CurrentState == DeviceState.NotWorking) { CurrentState = DeviceState.NeedsRecharge; _rechargeCompleteTime = currentTime.Add(TimeSpan.FromMinutes(RECHARGE_DURATION_SIM_MINUTES)); } }
        public override void Reset() { Recharge(); _activeUntil = TimeSpan.Zero; _emptyWaterTime = TimeSpan.Zero; }
        public override void Interact(ToolType tool, bool isElectricityOn, TimeSpan currentTime) { if (CurrentState == DeviceState.EmptyWater || CurrentState == DeviceState.NotWorking) StartRechargeCycle(currentTime); else if (IsManuallyActivatable && CurrentState == DeviceState.Working && isElectricityOn) ActivateManual(currentTime); }
    }

    public class ThermostatDevice : SmartDeviceBase, IOnOffToggleable
    {
        private double _targetTemperature = 22;
        public double TargetTemperature 
        { 
            get => _targetTemperature; 
            set 
            { 
                if (Math.Abs(_targetTemperature - value) > 0.01)
                {
                    _targetTemperature = Math.Max(16, Math.Min(28, value)); 
                    OnPropertyChanged();
                    string roomName = AssociatedRoom?.Name ?? "невідома область";
                    if (roomName == "Спальня 1" || roomName == "Спальня 2" || roomName == "Вітальня")
                    {
                        MainWindowContext?.LogEvent($"В області приміщення {roomName} бажане значення температури в термостаті було змінено на {_targetTemperature:F1}°C");
                    }
                }
            } 
        }
        private int _targetHumidity = 50;
        public int TargetHumidity 
        { 
            get => _targetHumidity; 
            set 
            { 
                if (_targetHumidity != value)
                {
                    _targetHumidity = (int)Math.Max(20, Math.Min(80, value)); 
                    OnPropertyChanged();
                    string roomName = AssociatedRoom?.Name ?? "невідома область";
                    if (roomName == "Спальня 1" || roomName == "Спальня 2" || roomName == "Вітальня")
                    {
                        MainWindowContext?.LogEvent($"В області приміщення {roomName} бажане значення вологості в термостаті було змінено на {_targetHumidity}%");
                    }
                }
            } 
        }
        public ThermostatDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { CurrentState = DeviceState.Working; }
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Working: return "Працює";
                case DeviceState.Off: return "Вимкнено";
                default: return base.GetStatusName(state);
            }
        }
        
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? $"Працює (ціль: {TargetTemperature:F1}°C, {TargetHumidity}%)" : "Вимкнено";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn && CurrentState == DeviceState.Working) TurnOff(); else if (isElectricityOn && CurrentState == DeviceState.Off) TurnOn(true); }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; } }
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
        { 
            if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } 
            var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); 
            if (thermostat != null && environment != null) 
            { 
                int targetTemp = (int)Math.Round(thermostat.TargetTemperature);
                if (environment.CurrentTemperature < targetTemp) 
                { 
                    if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); 
                } 
                else if (environment.CurrentTemperature >= targetTemp)
                { 
                    if (CurrentState == DeviceState.Working) TurnOff(); 
                } 
            } 
            else if (CurrentState == DeviceState.Working) TurnOff(); 
        }
    }
    public class ConditionerDevice : ClimateControlUnit
    {
        public ConditionerDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { 
            if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } 
            var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); 
            if (thermostat != null && environment != null) 
            { 
                int targetTemp = (int)Math.Round(thermostat.TargetTemperature);
                if (environment.CurrentTemperature > targetTemp) 
                { 
                    if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); 
                } 
                else if (environment.CurrentTemperature <= targetTemp)
                { 
                    if (CurrentState == DeviceState.Working) TurnOff(); 
                } 
            } 
            else if (CurrentState == DeviceState.Working) TurnOff(); 
        }
    }
    public class HumidifierDevice : ClimateControlUnit
    {
        public HumidifierDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { 
            if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } 
            var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); 
            if (thermostat != null && environment != null) 
            { 
                if (environment.CurrentHumidity < thermostat.TargetHumidity) 
                { 
                    if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); 
                } 
                else if (environment.CurrentHumidity >= thermostat.TargetHumidity)
                { 
                    if (CurrentState == DeviceState.Working) TurnOff(); 
                } 
            } 
            else if (CurrentState == DeviceState.Working) TurnOff(); 
        }
    }
    public class DehumidifierDevice : ClimateControlUnit
    {
        public DehumidifierDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { 
            if (!isElectricityOn) { if (CurrentState != DeviceState.Off) TurnOff(); return; } 
            var thermostat = environment?.Devices.OfType<ThermostatDevice>().FirstOrDefault(t => t.CurrentState == DeviceState.Working); 
            if (thermostat != null && environment != null) 
            { 
                if (environment.CurrentHumidity > thermostat.TargetHumidity) 
                { 
                    if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); 
                } 
                else if (environment.CurrentHumidity <= thermostat.TargetHumidity)
                { 
                    if (CurrentState == DeviceState.Working) TurnOff(); 
                } 
            } 
            else if (CurrentState == DeviceState.Working) TurnOff(); 
        }
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
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Працює" : "Вимкнена";
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { if (!isElectricityOn && CurrentState == DeviceState.Working) { TurnOff(); return; } if (IsHumidityControlled && environment != null) { if (environment.CurrentHumidity > HUMIDITY_THRESHOLD_VE1) { if (CurrentState == DeviceState.Off && isElectricityOn) TurnOn(isElectricityOn); } else { if (CurrentState == DeviceState.Working) TurnOff(); } } }
        public void TurnOn(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Увімкнено."); } }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
        public void ActivateManual(bool isElectricityOn) { if (isElectricityOn && CurrentState != DeviceState.Working) { CurrentState = DeviceState.Working; Log("Активовано (плита)."); } }
    }

    public class SolarPanelDevice : SmartDeviceBase, IOnOffToggleable
    {
        private bool _isManuallyDisabled = false;
        
        public SolarPanelDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        
        public override string CurrentStateDescription => CurrentState == DeviceState.Working ? "Генерація енергії" : (_isManuallyDisabled ? "Вимкнені вручну" : "Неактивні");
        
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        { 
            DeviceState prevState = CurrentState; 
            
            // Якщо вимкнені вручну, не працюють незалежно від часу
            if (_isManuallyDisabled)
            {
                if (CurrentState != DeviceState.Off)
                {
                    CurrentState = DeviceState.Off;
                    if (prevState != CurrentState) Log("Вимкнені вручну.");
                }
                return;
            }
            
            // Автоматичний режим: працюють з 8:00 до 16:00
            if (currentTime.Hours >= 8 && currentTime.Hours < 16) 
                CurrentState = DeviceState.Working; 
            else 
                CurrentState = DeviceState.Off; 
                
            if (prevState != CurrentState) 
                Log(CurrentState == DeviceState.Working ? "Почали генерацію." : "Припинили генерацію."); 
        }
        
        public void TurnOn(bool isElectricityOn) 
        { 
            if (_isManuallyDisabled)
            {
                _isManuallyDisabled = false; 
                Log("Увімкнені вручну (автоматичний режим).");
            }
        }
        
        public void TurnOff() 
        { 
            if (!_isManuallyDisabled)
            {
                _isManuallyDisabled = true; 
                CurrentState = DeviceState.Off;
                Log("Вимкнені вручну.");
            }
        }
        
        public void ToggleState(bool isElectricityOn) 
        { 
            if (_isManuallyDisabled) 
                TurnOn(isElectricityOn); 
            else 
                TurnOff(); 
        }
        
        public override void Reset() 
        { 
            base.Reset(); 
            _isManuallyDisabled = false; 
        }
    }

    public class SirenDevice : SmartDeviceBase
    {
        private readonly TimeSpan _activationDurationSim = TimeSpan.FromMinutes(30); 
        private TimeSpan _activeUntil;
        private bool _wasActivatedByBreach = false;
        
        public SirenDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) : base(name, id, room, position, size, mw) { }
        
        protected override string GetStatusName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Active: return "АКТИВНА";
                case DeviceState.Off: return "Вимкнена";
                default: return base.GetStatusName(state);
            }
        }
        
        public override string CurrentStateDescription => CurrentState == DeviceState.Active ? "СИРЕНА АКТИВНА!" : "Вимкнена";
        
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) 
        { 
            if (CurrentState == DeviceState.Active && currentTime > _activeUntil) 
            { 
                CurrentState = DeviceState.Off; 
                _wasActivatedByBreach = false;
            } 
        }
        
        public void Activate(TimeSpan currentTime) 
        { 
            if (CurrentState != DeviceState.Active) 
            { 
                CurrentState = DeviceState.Active; 
                _activeUntil = currentTime + _activationDurationSim;
                _wasActivatedByBreach = true;
                MainWindowContext?.LogEvent("Виклик Поліції");
                MainWindowContext?.StartAlarm();
            } 
        }
        
        public override void Reset() { base.Reset(); _activeUntil = TimeSpan.Zero; _wasActivatedByBreach = false; }
    }

    public class BatteryDevice : SmartDeviceBase
    {
        public double ChargeLevel { get; private set; } = 100.0;
        public bool IsCharging { get; set; } = false; 
        public bool IsDischarging { get; set; } = false;
        public int ActiveDeviceCount { get; set; } = 0;
        
        // Перехід на пер-хвилинні (симуляційні) ставки для стабільної швидкості
        private const double CHARGE_PER_MIN = 100.0 / 120.0; // 0→100% за 120 сим-хвилин (2 години)
        private const double BASE_DISCHARGE_PER_MIN = 0.05;  // базова розрядка, %/сим-хв
        private const double PER_DEVICE_DISCHARGE_PER_MIN = 0.046; // додатково за кожен активний критичний пристрій, %/сим-хв
        
        private bool _isEffectivelyPowering = false;
        private bool _lastLoggedFullyCharged = false;
        private bool _wasCharging = false;
        private bool _wasDischarging = false;
        private double _lastLoggedChargeLevel = 100.0; // Для відстеження змін заряду
        
        public BatteryDevice(string name, string id, Room room, Point position, Size size, MainWindow mw) 
            : base(name, id, room, position, size, mw) 
        { 
            CurrentState = DeviceState.Working; 
        }
        
        public override string CurrentStateDescription => $"Резерв: {ChargeLevel:F0}%" + 
            (CurrentState == DeviceState.Charging ? " (Зарядка)" : 
            (_isEffectivelyPowering ? $" (Живлення {ActiveDeviceCount} прист.)" : " (Готова)"));
        
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment)
        {
            _isEffectivelyPowering = IsDischarging && ChargeLevel > 0 && !isElectricityOn;
            DeviceState prevState = CurrentState;
            double elapsedSimMinutes = MainWindow.UPDATE_INTERVAL_SECONDS * 60 * MainWindow.SIMULATION_SPEED_FACTOR;

            if (IsCharging && ChargeLevel < 100.0)
            { 
                // Логуємо початок зарядки
                if (!_wasCharging)
                {
                    Log($"Почала зарядку. Поточний заряд: {ChargeLevel:F0}%");
                    _wasCharging = true;
                    _wasDischarging = false;
                    _lastLoggedChargeLevel = ChargeLevel;
                }
                
                double oldCharge = ChargeLevel;
                ChargeLevel = Math.Min(100.0, ChargeLevel + CHARGE_PER_MIN * elapsedSimMinutes); 
                
                // Логуємо кожні 5% зміни заряду під час зарядки
                if (Math.Floor(ChargeLevel / 5) > Math.Floor(_lastLoggedChargeLevel / 5))
                {
                    Log($"Зарядка: {ChargeLevel:F0}%");
                    _lastLoggedChargeLevel = ChargeLevel;
                }
                
                CurrentState = DeviceState.Charging;
                _lastLoggedFullyCharged = false;
            }
            else if (IsDischarging && ChargeLevel > 0 && !isElectricityOn)
            { 
                // Логуємо початок розрядження
                if (!_wasDischarging)
                {
                    Log($"Почала розрядження (живлення {ActiveDeviceCount} критичних пристроїв). Поточний заряд: {ChargeLevel:F0}%");
                    _wasDischarging = true;
                    _wasCharging = false;
                    _lastLoggedChargeLevel = ChargeLevel;
                }
                
                double oldCharge = ChargeLevel;
                // Розраховуємо швидкість розрядження на основі кількості активних пристроїв (у % за сим-хвилину)
                double dischargeRate = (BASE_DISCHARGE_PER_MIN + (ActiveDeviceCount * PER_DEVICE_DISCHARGE_PER_MIN));
                ChargeLevel = Math.Max(0, ChargeLevel - dischargeRate * elapsedSimMinutes); 
                
                // Логуємо кожні 5% зміни заряду під час розрядження
                if (Math.Floor(_lastLoggedChargeLevel / 5) > Math.Floor(ChargeLevel / 5))
                {
                    Log($"Розрядження: {ChargeLevel:F0}% (живлення {ActiveDeviceCount} пристроїв)");
                    _lastLoggedChargeLevel = ChargeLevel;
                }
                
                CurrentState = (ChargeLevel > 0) ? DeviceState.Working : DeviceState.EmptyWater;
            }
            else if (ChargeLevel >= 100.0 && IsCharging) 
            { 
                IsCharging = false; 
                CurrentState = DeviceState.Working; 
                
                // Логуємо "Повністю заряджена" тільки один раз
                if (!_lastLoggedFullyCharged)
                {
                    Log("Повністю заряджена: 100%");
                    _lastLoggedFullyCharged = true;
                    _wasCharging = false;
                }
            }
            else if (isElectricityOn && CurrentState != DeviceState.Charging) 
            {
                // Коли електрика увімкнена, а зарядка/розрядка припинилася
                if (_wasDischarging)
                {
                    Log($"Припинила розрядження. Залишок заряду: {ChargeLevel:F0}%");
                    _wasDischarging = false;
                }
                CurrentState = DeviceState.Working;
            }
            else
            {
                // Скидаємо прапорці якщо не заряджаємо і не розряджаємо
                if (!IsCharging && _wasCharging)
                {
                    _wasCharging = false;
                }
                if (!IsDischarging && _wasDischarging)
                {
                    _wasDischarging = false;
                }
            }

            if (ChargeLevel <= 0 && CurrentState != DeviceState.EmptyWater) 
            { 
                CurrentState = DeviceState.EmptyWater; 
                Log("Розряджена: 0%");
                _wasDischarging = false;
            }
            
            if (ChargeLevel > 0 && CurrentState == DeviceState.EmptyWater && (isElectricityOn || IsCharging)) 
            {
                CurrentState = DeviceState.Working;
            }
        }
        
        public override void Reset() 
        { 
            ChargeLevel = 100.0; 
            CurrentState = DeviceState.Working; 
            IsCharging = false; 
            IsDischarging = false; 
            ActiveDeviceCount = 0;
            _lastLoggedFullyCharged = false;
            _wasCharging = false;
            _wasDischarging = false;
            _lastLoggedChargeLevel = 100.0;
        }
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
        public override void UpdateState(TimeSpan currentTime, bool isElectricityOn, Room environment) 
        { 
            // Плита є газовою і працює без електроенергії
        }
        public void TurnOn(bool isElectricityOn) 
        { 
            // Плита може працювати без електроенергії
            if (CurrentState != DeviceState.Working) 
            { 
                CurrentState = DeviceState.Working; 
                Log("Увімкнено."); 
            } 
        }
        public void TurnOff() { if (CurrentState != DeviceState.Off) { CurrentState = DeviceState.Off; Log("Вимкнено."); } }
        public void ToggleState(bool isElectricityOn) { if (CurrentState == DeviceState.Off) TurnOn(isElectricityOn); else TurnOff(); }
    }

    public class Room : INotifyPropertyChanged
    {
        private readonly MainWindow _mainWindowContext;
        public string Name { get; }
        public Rect AreaRect { get; }
        public List<SmartDeviceBase> Devices { get; } = new List<SmartDeviceBase>();
        private int _currentTemperature;
        public int CurrentTemperature { get => _currentTemperature; set { _currentTemperature = value; OnPropertyChanged(); } }
        public int DefaultTemperature { get; set; } = 20;
        private int _currentHumidity;
        public int CurrentHumidity { get => _currentHumidity; set { _currentHumidity = value; OnPropertyChanged(); } }
        public int DefaultHumidity { get; set; } = 50;
        private bool _hasFire;
        public bool HasFire 
        { 
            get => _hasFire; 
            set 
            { 
                if (_hasFire != value) 
                { 
                    _hasFire = value; 
                    OnPropertyChanged(); 
                    if (value) 
                        _mainWindowContext.LogEvent($"В області приміщення {Name} виникла пожежа");
                    else 
                        _mainWindowContext.LogEvent($"Пожежу в області приміщення {Name} погашено");
                } 
            } 
        }
        private bool _hasMotion;
        public bool HasMotion { get => _hasMotion; set { _hasMotion = value; OnPropertyChanged(); } }
        public TimeSpan MotionEndTime { get; set; }
        private bool _isBreached;
        public bool IsBreached 
        { 
            get => _isBreached; 
            set 
            { 
                if (_isBreached != value) 
                { 
                    _isBreached = value; 
                    OnPropertyChanged(); 
                    if (value) 
                        _mainWindowContext.LogEvent($"В області приміщення {Name} виявлено взлом");
                    else 
                        _mainWindowContext.LogEvent($"Загрозу взлому в області приміщення {Name} усунуто");
                } 
            } 
        }

        public Room(string name, Rect areaRect, MainWindow mainWindow)
        { Name = name; AreaRect = areaRect; _mainWindowContext = mainWindow; CurrentTemperature = DefaultTemperature; CurrentHumidity = DefaultHumidity; }

        public void UpdateEnvironment(TimeSpan elapsedSimTimePerTick, bool isElectricityOn)
        {
            if (HasMotion && _mainWindowContext.CurrentSimTime > MotionEndTime) HasMotion = false;

            if (isElectricityOn)
            {

                double tempChange = 0;
                foreach (var heater in Devices.OfType<HeaterDevice>().Where(d => d.CurrentState == DeviceState.Working)) tempChange += 1;
                foreach (var conditioner in Devices.OfType<ConditionerDevice>().Where(d => d.CurrentState == DeviceState.Working)) tempChange -= 1;
                CurrentTemperature = (int)Math.Round(Math.Max(-273, CurrentTemperature + tempChange));

                double humidityChange = 0;
                foreach (var humidifier in Devices.OfType<HumidifierDevice>().Where(d => d.CurrentState == DeviceState.Working)) humidityChange += 1;
                foreach (var dehumidifier in Devices.OfType<DehumidifierDevice>().Where(d => d.CurrentState == DeviceState.Working)) humidityChange -= 1;
                foreach (var fan in Devices.OfType<FanDevice>().Where(d => d.CurrentState == DeviceState.Working && d.IsHumidityControlled)) humidityChange -= 4;
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
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { if (!(value is DeviceState state) || !(parameter is string actionNumber)) return null; if (actionNumber == "Action1") { switch (state) { case DeviceState.Off: case DeviceState.Inactive: return "Увімк."; case DeviceState.Working: case DeviceState.Active: return "Вимк."; case DeviceState.Destroyed: return "Полаг."; case DeviceState.EmptyWater: return "Перезар."; case DeviceState.NeedsRecharge: return "Зарядка..."; case DeviceState.Charging: return "Зарядка..."; } } return null; }
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

