// MainWindow.xaml.cs

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DanawaRClient.Views
{
    public partial class MainView : UserControl
    {
        private Counter _counter;
        private DispatcherTimer _timer;
        private DataSender _dataSender;
        private int _sendCounter = 0;
        private const int SEND_INTERVAL = 3;

        public MainView()
        {
            InitializeComponent();

            _counter = new Counter();

            // 자동으로 디바이스 ID 생성
            string deviceId = GetAutoDeviceId();

            // 포트를 9000으로 수정
            _dataSender = new DataSender("http://10.10.21.127:9000", deviceId);

            System.Diagnostics.Debug.WriteLine($"디바이스 ID: {deviceId}");

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            this.Unloaded += MainView_Unloaded;
        }

        private string GetAutoDeviceId()
        {
            try
            {
                // MAC 주소 가져오기
                var mac = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                               n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .OrderBy(n => n.Name)
                    .FirstOrDefault()
                    ?.GetPhysicalAddress()
                    .ToString();

                if (string.IsNullOrEmpty(mac))
                    return "Agent-00";

                // MAC 주소를 숫자로 변환 (01~99)
                int hashCode = mac.GetHashCode();
                int number = Math.Abs(hashCode % 99) + 1; // 1~99

                return $"Agent-{number:D2}"; // Agent-01, Agent-02 형식
            }
            catch
            {
                return Environment.MachineName; // 실패 시 PC 이름
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                // CPU
                var cpuValue = _counter.PerformanceCPU.NextValue();
                cpuProgressBar.Value = cpuValue;
                cpuLabel.Content = cpuValue;
                // CPU 차트 업데이트
                if (cpuLineChart != null)
                {
                    cpuLineChart.AddValue(cpuValue);
                }
                // RAM
                var ramPercent = _counter.GetFreeRAMInPercent();
                ramProgressBar.Value = ramPercent;
                ramLabel.Content = ramPercent;
                ramUsedLabel.Content = _counter.GetUsedRAMInGBytes();
                ramFreeLabel.Content = _counter.GetFreeRAMInGBytes();
                // Disk Total Gauge
                if (diskTotalGauge != null)
                {
                    diskTotalGauge.GaugeValue = _counter.GetFreeSpaceTotalGauge();
                }
                diskSpaceTotalLabel.Content = _counter.GetFreeSpaceTotal();
                usedSpaceTotalLabel.Content = _counter.GetUsedSpaceLabel();
                freeSpaceTotalLabel.Content = _counter.GetFreeSpaceLabel();
                // Disk C Gauge
                if (diskCGauge != null)
                {
                    diskCGauge.GaugeValue = _counter.GetFreeSpaceCGauge();
                }
                diskCLabel.Content = _counter.GetFreeSpaceDiskC();
                // Disk D Gauge
                if (diskDGauge != null)
                {
                    diskDGauge.GaugeValue = _counter.GetFreeSpaceDGauge();
                }
                diskDLabel.Content = _counter.GetFreeSpaceDiskD();
                // Network
                var sent = _counter.GetNetworkSentBytes();
                var received = _counter.GetNetworkReceivedBytes();
                networkSentBytesLabel.Content = sent;
                networkReceivedBytesLabel.Content = received;
                // Temperature (CPU 온도)
                var temp = _counter.GetCPUTemperature();
                temperature.Content = temp > 0 ? temp : 0;

                // 서버로 데이터 전송 (3초마다)
                _sendCounter++;
                if (_sendCounter >= SEND_INTERVAL)
                {
                    _sendCounter = 0;
                    SendDataToServer(cpuValue, ramPercent, sent, received, temp);
                }
            }
            catch (Exception ex)
            {
                // 에러 발생시 무시 (다음 타이머에서 재시도)
                System.Diagnostics.Debug.WriteLine($"Timer error: {ex.Message}");
            }
        }

        private void MainView_Unloaded(object sender, RoutedEventArgs e)
        {
            // 리소스 정리
            _timer?.Stop();
            _counter?.Dispose();
            _dataSender?.Dispose();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _counter?.Dispose();
            _dataSender?.Dispose();
            Application.Current.Shutdown();
        }

        private async void SendDataToServer(
            double cpu, double ram, double netSent, double netReceived, double temp)
        {
            try
            {
                var diskUsage = _counter.GetFreeSpaceTotalGauge();

                var data = new SensorData
                {
                    CpuUsage = Math.Round(cpu, 2),
                    RamUsagePercent = Math.Round(ram, 2),
                    DiskUsagePercent = Math.Round(diskUsage, 2),
                    NetworkSent = Math.Round(netSent, 2),
                    NetworkReceived = Math.Round(netReceived, 2),
                    VirtualTemp = Math.Round(temp, 2)
                };

                await _dataSender.SendSensorDataAsync(data);
                System.Diagnostics.Debug.WriteLine("서버 전송 완료!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"전송 오류: {ex.Message}");
            }
        }
    }
}