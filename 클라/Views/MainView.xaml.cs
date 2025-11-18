using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DanawaRClient.ViewModels;
using LiveCharts;
using LiveCharts.Configurations;

namespace DanawaRClient.Views
{
    public partial class MainView : UserControl
    {
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _sendTimer;

        // Performance Counters
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;

        // Network tracking
        private long _lastSentBytes = 0;
        private long _lastReceivedBytes = 0;
        private DateTime _lastNetworkCheck = DateTime.Now;

        // Temperature monitoring
        private LibreHardwareMonitor.Hardware.Computer? _computer;
        private bool _useLibreHardware = true;
        private bool _useWmiAcpi = false;
        private bool _useOpenHardware = false;

        // TCP Client
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private readonly string _serverIp = "10.10.21.127"; // 서버 IP 주소
        private readonly int _serverPort = 9000;

        // Device ID
        private string _deviceId = "";

        // CPU Chart values
        private ChartValues<double> _cpuChartValues = new ChartValues<double>();
        private const int MaxChartPoints = 60; // 60초

        // ChartValues 프로퍼티 (LineChart 바인딩용)
        public ChartValues<double> ChartValues => _cpuChartValues;

        public MainView()
        {
            InitializeComponent();

            var vm = new MainViewModel();
            DataContext = vm;

            // LineChart DataContext 직접 설정
            cpuLineChart.DataContext = this;

            // Device ID 생성
            _deviceId = GenerateDeviceId();
            Debug.WriteLine($"[CLIENT] Device ID: {_deviceId}");

            InitializePerformanceCounters();
            InitializeTemperatureMonitoring();

            // TCP 연결
            _ = ConnectToServerAsync();

            // UI 업데이트 타이머 (1초마다)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // 서버 전송 타이머 (3초마다)
            _sendTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _sendTimer.Tick += SendTimer_Tick;
            _sendTimer.Start();
        }

        private string GenerateDeviceId()
        {
            try
            {
                // MAC 주소 기반으로 1~10 고정 번호 생성
                var mac = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                               n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => n.GetPhysicalAddress().ToString())
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(mac))
                {
                    // MAC 주소의 해시코드를 1~10 범위로 매핑
                    int hashCode = mac.GetHashCode();
                    int deviceNum = Math.Abs(hashCode % 10) + 1;

                    Debug.WriteLine($"[CLIENT] Generated Device ID from MAC: {deviceNum}");
                    return deviceNum.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Device ID generation error: {ex.Message}");
            }

            // MAC 주소를 못 찾으면 랜덤
            var random = new Random();
            int fallbackNum = random.Next(1, 11);
            Debug.WriteLine($"[CLIENT] Generated Device ID (fallback random): {fallbackNum}");
            return fallbackNum.ToString();
        }

        private async Task ConnectToServerAsync()
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_serverIp, _serverPort);
                _networkStream = _tcpClient.GetStream();
                Debug.WriteLine("[CLIENT] Connected to server");

                // 서버 메시지 수신 대기
                _ = ReceiveServerMessagesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Connection error: {ex.Message}");
            }
        }

        private async Task ReceiveServerMessagesAsync()
        {
            if (_networkStream == null) return;

            var buffer = new byte[1024];
            try
            {
                while (_tcpClient?.Connected == true)
                {
                    int bytes = await _networkStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytes <= 0) break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, bytes);
                    Debug.WriteLine($"[CLIENT] Received from server: {msg}");

                    // JSON 파싱하여 메시지 타입 확인
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(msg);
                        if (jsonDoc.RootElement.TryGetProperty("type", out var typeElement))
                        {
                            string msgType = typeElement.GetString() ?? "";

                            if (msgType == "SHUTDOWN")
                            {
                                Debug.WriteLine("[CLIENT] Shutdown command received - shutting down PC");

                                // PC 종료 명령 실행
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "shutdown",
                                    Arguments = "/s /t 0",  // /s = 종료, /t 0 = 0초 후
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                });

                                // 프로그램도 종료
                                await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
                            }
                            else if (msgType == "AUTH_CODE")
                            {
                                if (jsonDoc.RootElement.TryGetProperty("code", out var codeElement))
                                {
                                    string code = codeElement.GetString() ?? "";
                                    Debug.WriteLine($"[CLIENT] Auth code received: {code}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CLIENT] Message parse error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Receive error: {ex.Message}");
            }
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

                // 첫 값 읽기 (초기화)
                _cpuCounter.NextValue();
                _ramCounter.NextValue();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Performance counter init error: {ex.Message}");
            }
        }

        private void InitializeTemperatureMonitoring()
        {
            Debug.WriteLine("[CLIENT] Starting temperature monitoring initialization...");

            // 1순위: LibreHardwareMonitor 시도
            try
            {
                Debug.WriteLine("[CLIENT] Trying LibreHardwareMonitor...");
                _computer = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsCpuEnabled = true,
                    IsMotherboardEnabled = true
                };
                _computer.Open();

                // 실제로 온도 값을 읽을 수 있는지 테스트
                bool hasValidTemp = false;
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                    {
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature &&
                                sensor.Value.HasValue && sensor.Value.Value > 0)
                            {
                                hasValidTemp = true;
                                break;
                            }
                        }
                    }
                    if (hasValidTemp) break;
                }

                if (hasValidTemp)
                {
                    _useLibreHardware = true;
                    _useWmiAcpi = false;
                    _useOpenHardware = false;
                    Debug.WriteLine("[CLIENT] Temperature: Using LibreHardwareMonitor");
                    return;
                }
                else
                {
                    Debug.WriteLine("[CLIENT] LibreHardwareMonitor: No valid temperature values, trying fallback...");
                    _computer.Close();
                    _computer = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] LibreHardwareMonitor failed: {ex.Message}");
                _useLibreHardware = false;
            }

            // 2순위: WMI ACPI 시도
            try
            {
                Debug.WriteLine("[CLIENT] Trying WMI ACPI...");
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                var collection = searcher.Get();

                if (collection.Count > 0)
                {
                    _useWmiAcpi = true;
                    _useLibreHardware = false;
                    _useOpenHardware = false;
                    Debug.WriteLine($"[CLIENT] Temperature: Using WMI ACPI ({collection.Count} sensors found)");

                    // 첫 값 테스트
                    foreach (System.Management.ManagementObject obj in collection)
                    {
                        var temp = Convert.ToDouble(obj["CurrentTemperature"]);
                        float celsius = (float)((temp - 2732) / 10.0);
                        Debug.WriteLine($"[CLIENT] WMI ACPI test reading: {celsius}°C");
                        break;
                    }
                    return;
                }
                Debug.WriteLine("[CLIENT] WMI ACPI: No sensors found");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] WMI ACPI failed: {ex.Message}");
            }

            // 3순위: OpenHardwareMonitor WMI 시도
            try
            {
                Debug.WriteLine("[CLIENT] Trying OpenHardwareMonitor WMI...");
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\OpenHardwareMonitor", "SELECT * FROM Sensor WHERE SensorType='Temperature'");
                var collection = searcher.Get();

                if (collection.Count > 0)
                {
                    _useOpenHardware = true;
                    _useLibreHardware = false;
                    _useWmiAcpi = false;
                    Debug.WriteLine($"[CLIENT] Temperature: Using OpenHardwareMonitor WMI ({collection.Count} sensors found)");
                    return;
                }
                Debug.WriteLine("[CLIENT] OpenHardwareMonitor WMI: No sensors found");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] OpenHardwareMonitor WMI failed: {ex.Message}");
            }

            Debug.WriteLine("[CLIENT] Temperature: No method available - all methods failed");
        }

        private float GetTemperature()
        {
            // LibreHardwareMonitor
            if (_useLibreHardware && _computer != null)
            {
                try
                {
                    foreach (var hardware in _computer.Hardware)
                    {
                        hardware.Update();

                        Debug.WriteLine($"[CLIENT] Checking hardware: {hardware.Name} (Type: {hardware.HardwareType})");

                        if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                        {
                            Debug.WriteLine($"[CLIENT] Found CPU hardware: {hardware.Name}");
                            Debug.WriteLine($"[CLIENT] CPU has {hardware.Sensors.Length} sensors");

                            // 모든 센서 출력
                            foreach (var sensor in hardware.Sensors)
                            {
                                Debug.WriteLine($"[CLIENT]   Sensor: {sensor.Name}, Type: {sensor.SensorType}, Value: {sensor.Value}");
                            }

                            // Package 온도를 우선적으로 찾음
                            foreach (var sensor in hardware.Sensors)
                            {
                                if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
                                {
                                    if (sensor.Name.Contains("Package") || sensor.Name.Contains("CPU"))
                                    {
                                        float temp = sensor.Value ?? 0f;
                                        if (temp > 0)
                                        {
                                            Debug.WriteLine($"[CLIENT] ✓ Temperature reading: {temp}°C from {sensor.Name}");
                                            return temp;
                                        }
                                    }
                                }
                            }

                            // Package를 못 찾으면 첫 번째 온도 센서 사용
                            foreach (var sensor in hardware.Sensors)
                            {
                                if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
                                {
                                    float temp = sensor.Value ?? 0f;
                                    if (temp > 0)
                                    {
                                        Debug.WriteLine($"[CLIENT] ✓ Temperature reading (fallback): {temp}°C from {sensor.Name}");
                                        return temp;
                                    }
                                }
                            }

                            Debug.WriteLine("[CLIENT] ✗ No valid temperature sensor with value > 0 found in CPU");
                        }

                        // Motherboard도 체크
                        if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Motherboard)
                        {
                            Debug.WriteLine($"[CLIENT] Checking Motherboard subhardware...");
                            foreach (var subhardware in hardware.SubHardware)
                            {
                                subhardware.Update();
                                Debug.WriteLine($"[CLIENT]   SubHardware: {subhardware.Name} (Type: {subhardware.HardwareType})");

                                if (subhardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                                {
                                    foreach (var sensor in subhardware.Sensors)
                                    {
                                        if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
                                        {
                                            float temp = sensor.Value ?? 0f;
                                            if (temp > 0)
                                            {
                                                Debug.WriteLine($"[CLIENT] ✓ Temperature from SubHardware: {temp}°C from {sensor.Name}");
                                                return temp;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    Debug.WriteLine("[CLIENT] ✗ LibreHardware: No valid temperature sensor found");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CLIENT] LibreHardware read error: {ex.Message}");
                }
            }

            // WMI ACPI
            if (_useWmiAcpi)
            {
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        @"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        var temp = Convert.ToDouble(obj["CurrentTemperature"]);
                        float celsius = (float)((temp - 2732) / 10.0);
                        Debug.WriteLine($"[CLIENT] ✓ Temperature reading (WMI ACPI): {celsius}°C");
                        return celsius;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CLIENT] WMI ACPI read error: {ex.Message}");
                }
            }

            // OpenHardwareMonitor WMI
            if (_useOpenHardware)
            {
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        @"root\OpenHardwareMonitor",
                        "SELECT * FROM Sensor WHERE SensorType='Temperature' AND Name LIKE '%CPU%'");

                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        float temp = Convert.ToSingle(obj["Value"]);
                        Debug.WriteLine($"[CLIENT] ✓ Temperature reading (OpenHardware): {temp}°C");
                        return temp;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CLIENT] OpenHardware read error: {ex.Message}");
                }
            }

            return 0f;
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // CPU
                float cpu = _cpuCounter?.NextValue() ?? 0f;
                cpuProgressBar.Value = cpu;
                cpuLabel.Content = cpu;

                // CPU 차트에 값 추가
                _cpuChartValues.Add(cpu);
                if (_cpuChartValues.Count > MaxChartPoints)
                {
                    _cpuChartValues.RemoveAt(0);
                }

                // RAM
                float ramPercent = _ramCounter?.NextValue() ?? 0f;
                ramProgressBar.Value = ramPercent;
                ramLabel.Content = ramPercent;

                // RAM 용량 계산 (PerformanceCounter 사용)
                try
                {
                    var totalRamCounter = new PerformanceCounter("Memory", "Available Bytes");
                    var commitLimitCounter = new PerformanceCounter("Memory", "Commit Limit");

                    ulong availableBytes = (ulong)totalRamCounter.NextValue();
                    ulong commitLimit = (ulong)commitLimitCounter.NextValue();

                    float ramTotalGB = commitLimit / (1024f * 1024f * 1024f);
                    float ramUsedGB = ramTotalGB * (ramPercent / 100f);
                    float ramFreeGB = ramTotalGB - ramUsedGB;

                    ramUsedLabel.Content = ramUsedGB;
                    ramFreeLabel.Content = ramFreeGB;

                    totalRamCounter.Dispose();
                    commitLimitCounter.Dispose();
                }
                catch
                {
                    ramUsedLabel.Content = 0f;
                    ramFreeLabel.Content = 0f;
                }

                // Disk
                var drives = System.IO.DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed)
                    .ToList();

                long totalSpace = drives.Sum(d => d.TotalSize);
                long freeSpace = drives.Sum(d => d.AvailableFreeSpace);
                long usedSpace = totalSpace - freeSpace;

                float totalGB = totalSpace / (1024f * 1024f * 1024f);
                float usedGB = usedSpace / (1024f * 1024f * 1024f);
                float freeGB = freeSpace / (1024f * 1024f * 1024f);
                float diskPercent = (float)usedSpace / totalSpace;

                diskSpaceTotalLabel.Content = diskPercent;
                usedSpaceTotalLabel.Content = usedGB;
                freeSpaceTotalLabel.Content = freeGB;

                if (DataContext is MainViewModel vmDisk)
                {
                    vmDisk.DiskTotalGaugeValue = diskPercent * 100;
                }

                // C 드라이브
                var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C"));
                if (cDrive != null)
                {
                    float cPercent = 1f - ((float)cDrive.AvailableFreeSpace / cDrive.TotalSize);
                    diskCLabel.Content = cPercent;  // ← Label은 그대로 (P1 포맷이 자동 변환)
                    diskCGauge.GaugeValue = cPercent * 100;  // ← 게이지만 100 곱하기
                }

                // D 드라이브
                var dDrive = drives.FirstOrDefault(d => d.Name.StartsWith("D"));
                if (dDrive != null)
                {
                    float dPercent = 1f - ((float)dDrive.AvailableFreeSpace / dDrive.TotalSize);
                    diskDLabel.Content = dPercent;  // ← Label은 그대로 (P1 포맷이 자동 변환)
                    diskDGauge.GaugeValue = dPercent * 100;  // ← 게이지만 100 곱하기

                    Debug.WriteLine($"[CLIENT] D Drive: {dPercent:P1} ({dPercent * 100:F1}% for gauge)");
                }
                else
                {
                    Debug.WriteLine("[CLIENT] D Drive not found");
                }

                // Network
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                               n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                long totalSent = interfaces.Sum(n => n.GetIPv4Statistics().BytesSent);
                long totalReceived = interfaces.Sum(n => n.GetIPv4Statistics().BytesReceived);

                var now = DateTime.Now;
                var elapsed = (now - _lastNetworkCheck).TotalSeconds;

                if (elapsed > 0 && _lastSentBytes > 0)
                {
                    float sentMbps = (float)(((totalSent - _lastSentBytes) / elapsed) / (1024f * 1024f) * 8);
                    float receivedMbps = (float)(((totalReceived - _lastReceivedBytes) / elapsed) / (1024f * 1024f) * 8);

                    networkSentBytesLabel.Content = sentMbps;
                    networkReceivedBytesLabel.Content = receivedMbps;
                }

                _lastSentBytes = totalSent;
                _lastReceivedBytes = totalReceived;
                _lastNetworkCheck = now;

                // Temperature
                float temp = GetTemperature();
                temperature.Content = temp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Update error: {ex.Message}");
            }
        }

        private async void SendTimer_Tick(object? sender, EventArgs e)
        {
            if (_tcpClient?.Connected != true || _networkStream == null)
            {
                Debug.WriteLine("[CLIENT] Not connected to server, attempting reconnect...");
                await ConnectToServerAsync();
                return;
            }

            try
            {
                var data = new SensorData
                {
                    DeviceID = _deviceId,
                    CpuUsage = ParseFloat(cpuLabel.Content),
                    RamUsagePercent = ParseFloat(ramLabel.Content),
                    DiskUsagePercent = ParseFloat(diskSpaceTotalLabel.Content) * 100,
                    NetworkSent = ParseFloat(networkSentBytesLabel.Content),
                    NetworkReceived = ParseFloat(networkReceivedBytesLabel.Content),
                    VirtualTemp = ParseFloat(temperature.Content)
                };

                string json = JsonSerializer.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await _networkStream.WriteAsync(bytes, 0, bytes.Length);
                Debug.WriteLine($"[CLIENT] Sent to server: {json}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Send error: {ex.Message}");
            }
        }

        private float ParseFloat(object? content)
        {
            if (content == null) return 0f;
            if (float.TryParse(content.ToString(), out float result))
                return result;
            return 0f;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // Cleanup
        public void Cleanup()
        {
            _updateTimer?.Stop();
            _sendTimer?.Stop();
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            _computer?.Close();
            _networkStream?.Close();
            _tcpClient?.Close();
        }
    }

    // 서버와 동일한 데이터 구조
    // 서버와 동일한 데이터 구조
    public class SensorData
    {
        public string DeviceID { get; set; } = "";
        public double CpuUsage { get; set; }
        public double RamUsagePercent { get; set; }
        public double DiskUsagePercent { get; set; }
        public double NetworkSent { get; set; }
        public double NetworkReceived { get; set; }
        public double VirtualTemp { get; set; }
    }
}