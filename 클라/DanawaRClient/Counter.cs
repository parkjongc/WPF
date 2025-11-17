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
        public PerformanceCounter? FreeSpaceDiskD { get; set; }  // nullable로 변경

        public PerformanceCounter SentBytesPerSecond { get; set; }
        public PerformanceCounter ReceivedBytesPerSecond { get; set; }

        private readonly double _totalDiskSpaceGB;
        private readonly double _totalRAMInMB;

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

        // Network 메서드들 수정
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

        // CPU 온도 가져오기
        public double GetCPUTemperature()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    return (temp - 2732) / 10.0;
                }
            }
            catch
            {
                // 온도를 가져올 수 없는 경우
            }

            return 0;
        }
    }
}