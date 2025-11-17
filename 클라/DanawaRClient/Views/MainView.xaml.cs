// MainWindow.xaml.cs

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DanawaRClient.Views
{
    public partial class MainView : UserControl
    {
        private Counter _counter;
        private DispatcherTimer _timer;

        public MainView()
        {
            InitializeComponent();

            _counter = new Counter();
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                // CPU
                var cpuValue = _counter.PerformanceCPU.NextValue();
                cpuProgressBar.Value = cpuValue;
                cpuLabel.Content = cpuValue;

                // CPU 차트 업데이트 - LineChart 사용
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

                // Temperature
                var temp = _counter.GetCPUTemperature();
                temperature.Content = temp > 0 ? temp : 0;
            }
            catch (Exception ex)
            {
                // 에러 발생시 무시 (다음 타이머에서 재시도)
                System.Diagnostics.Debug.WriteLine($"Timer error: {ex.Message}");
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            Application.Current.Shutdown();
        }
    }
}