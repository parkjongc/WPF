using LibreHardwareMonitor.Hardware;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace DanawaRClient
{
    public class Counter
    {
        public PerformanceCounter PerformanceCPU { get; set; }
        public PerformanceCounter TimeCPU { get; set; }
        public PerformanceCounter OS_CPU { get; set; }
        public PerformanceCounter UserCPU { get; set; }

        public PerformanceCounter PerformanceRAM { get; set; }
        public PerformanceCounter FreeRAM { get; set; }

        public PerformanceCounter FreeSpaceDiskTotal { get; set; }
        public PerformanceCounter FreeSpaceDiskC { get; set; }
        public PerformanceCounter? FreeSpaceDiskD { get; set; }

        public PerformanceCounter SentBytesPerSecond { get; set; }
        public PerformanceCounter ReceivedBytesPerSecond { get; set; }

        private readonly double _totalDiskSpaceGB;
        private readonly double _totalRAMInMB;

        // LibreHardwareMonitor 추가
        private Computer _computer;

        public Counter()
        {
            // CPU Counters
            PerformanceCPU = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            TimeCPU = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            OS_CPU = new PerformanceCounter("Processor", "% Privileged time", "_Total");
            UserCPU = new PerformanceCounter("Processor", "% User Time", "_Total");

            // RAM Counters
            PerformanceRAM = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            FreeRAM = new PerformanceCounter("Memory", "Available MBytes");

            // 전체 RAM 용량 계산 (WMI 사용)
            _totalRAMInMB = GetTotalRAMInMB();

            // Disk Counters
            FreeSpaceDiskTotal = new PerformanceCounter("LogicalDisk", "% Free Space", "_Total");
            FreeSpaceDiskC = new PerformanceCounter("LogicalDisk", "% Free Space", "C:");

            // 전체 디스크 용량 계산 (GB)
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            _totalDiskSpaceGB = drives.Sum(d => d.TotalSize) / (1024.0 * 1024.0 * 1024.0);

            // D 드라이브 존재 확인 후 생성
            if (DriveInfo.GetDrives().Any(d => d.Name.StartsWith("D:") && d.IsReady))
            {
                FreeSpaceDiskD = new PerformanceCounter("LogicalDisk", "% Free Space", "D:");
            }

            // 네트워크 어댑터 자동 탐색
            var category = new PerformanceCounterCategory("Network Interface");
            var instances = category.GetInstanceNames();

            var adapter = instances.FirstOrDefault(i =>
                !i.Contains("Miniport") &&
                !i.Contains("Loopback") &&
                !i.Contains("isatap") &&
                !i.Contains("Kernel") &&
                !i.Contains("Virtual")
            );

            if (adapter == null)
                throw new Exception("사용 가능한 네트워크 어댑터가 없습니다.");

            SentBytesPerSecond = new PerformanceCounter("Network Interface", "Bytes Sent/sec", adapter);
            ReceivedBytesPerSecond = new PerformanceCounter("Network Interface", "Bytes Received/sec", adapter);

            // LibreHardwareMonitor 초기화
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsMotherboardEnabled = true
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibreHardwareMonitor 초기화 실패: {ex.Message}");
            }
        }

        // WMI를 사용해서 전체 RAM 용량 가져오기
        private double GetTotalRAMInMB()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var totalBytes = Convert.ToDouble(obj["TotalPhysicalMemory"]);
                    return totalBytes / (1024.0 * 1024.0);
                }
            }
            catch
            {
                // 기본값 반환 (16GB)
                return 16384;
            }

            return 16384;
        }

        // RAM 메서드들
        public double GetFreeRAMInPercent()
        {
            return 100 - (FreeRAM.NextValue() * 100 / _totalRAMInMB);
        }

        public double GetFreeRAMInGBytes()
        {
            return FreeRAM.NextValue() / 1024.0;
        }

        public double GetUsedRAMInGBytes()
        {
            return (_totalRAMInMB - FreeRAM.NextValue()) / 1024.0;
        }

        // Disk 메서드들
        public double GetFreeSpaceTotal()
        {
            return 1 - (FreeSpaceDiskTotal.NextValue() / 100);
        }

        public double GetFreeSpaceDiskC()
        {
            return 1 - (FreeSpaceDiskC.NextValue() / 100);
        }

        public double GetFreeSpaceDiskD()
        {
            if (FreeSpaceDiskD == null)
                return 0;
            return 1 - (FreeSpaceDiskD.NextValue() / 100);
        }

        public double GetFreeSpaceLabel()
        {
            return FreeSpaceDiskTotal.NextValue() * _totalDiskSpaceGB / 100;
        }

        public double GetUsedSpaceLabel()
        {
            return _totalDiskSpaceGB - GetFreeSpaceLabel();
        }

        // Network 메서드들
        public double GetNetworkSentBytes()
        {
            try
            {
                var bytes = SentBytesPerSecond.NextValue();
                return Math.Round(bytes * 8 / 1000000.0, 1); // Mbps로 변환
            }
            catch
            {
                return 0;
            }
        }

        public double GetNetworkReceivedBytes()
        {
            try
            {
                var bytes = ReceivedBytesPerSecond.NextValue();
                return Math.Round(bytes * 8 / 1000000.0, 1); // Mbps로 변환
            }
            catch
            {
                return 0;
            }
        }

        // Gauge용 메서드들
        public double GetFreeSpaceTotalGauge()
        {
            return 100 - FreeSpaceDiskTotal.NextValue();
        }

        public double GetFreeSpaceCGauge()
        {
            return 100 - FreeSpaceDiskC.NextValue();
        }

        public double GetFreeSpaceDGauge()
        {
            if (FreeSpaceDiskD == null)
                return 0;
            return 100 - FreeSpaceDiskD.NextValue();
        }

        // CPU 온도 가져오기 (여러 방법 시도)
        public double GetCPUTemperature()
        {
            // 방법 1: LibreHardwareMonitor
            var temp = GetTemperatureFromLibre();
            if (temp > 0)
            {
                Debug.WriteLine($"[온도] LibreHardwareMonitor: {temp}°C");
                return temp;
            }

            // 방법 2: WMI MSAcpi_ThermalZoneTemperature
            temp = GetTemperatureFromWMI();
            if (temp > 0)
            {
                Debug.WriteLine($"[온도] WMI ACPI: {temp}°C");
                return temp;
            }

            // 방법 3: OpenHardwareMonitor WMI (백그라운드에서 실행 중일 때)
            temp = GetTemperatureFromOHM();
            if (temp > 0)
            {
                Debug.WriteLine($"[온도] OpenHardwareMonitor: {temp}°C");
                return temp;
            }

            Debug.WriteLine("[온도] 모든 방법 실패");
            return 0; // 모든 방법 실패
        }

        private double GetTemperatureFromLibre()
        {
            if (_computer == null)
                return 0;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        // 모든 온도 센서 수집
                        var temps = hardware.Sensors
                            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0)
                            .ToList();

                        if (temps.Any())
                        {
                            // Package 온도 우선
                            var packageTemp = temps.FirstOrDefault(s =>
                                s.Name.Contains("Package") || s.Name.Contains("CPU"));
                            if (packageTemp != null)
                                return packageTemp.Value.Value;

                            // Core Average
                            var avgTemp = temps.FirstOrDefault(s => s.Name.Contains("Average"));
                            if (avgTemp != null)
                                return avgTemp.Value.Value;

                            // Core Max
                            var maxTemp = temps.FirstOrDefault(s => s.Name.Contains("Max"));
                            if (maxTemp != null)
                                return maxTemp.Value.Value;

                            // 첫 번째 코어
                            return temps.First().Value.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibreHardwareMonitor 온도 읽기 실패: {ex.Message}");
            }

            return 0;
        }

        private double GetTemperatureFromWMI()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    var celsius = (temp - 2732) / 10.0;
                    if (celsius > 0 && celsius < 150)
                        return celsius;
                }
            }
            catch { }

            return 0;
        }

        private double GetTemperatureFromOHM()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    @"root\OpenHardwareMonitor",
                    "SELECT * FROM Sensor WHERE SensorType='Temperature' AND Name LIKE '%CPU%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var value = Convert.ToDouble(obj["Value"]);
                    if (value > 0 && value < 150)
                        return value;
                }
            }
            catch { }

            return 0;
        }

        // Dispose 메서드 추가 (리소스 정리)
        public void Dispose()
        {
            try
            {
                _computer?.Close();
            }
            catch { }
        }
    }
}