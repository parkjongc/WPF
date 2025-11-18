using System;                                  // 기본 시스템 기능
using System.Collections.ObjectModel;          // ObservableCollection (바인딩용 컬렉션)
using System.ComponentModel;                   // INotifyPropertyChanged
using System.Configuration;                    // DB 커넥션 스트링
using System.Linq;                             // LINQ (정렬/FirstOrDefault 등)
using System.Threading.Tasks;                  // async/await
using System.Windows;                          // WPF Window 기본
using System.Windows.Threading;                // DispatcherTimer (주기적 갱신)
using MySqlConnector;                          // MySQL 접속 (MySqlConnector 패키지 사용 시)
// using MySql.Data.MySqlClient;               // 만약 MySql.Data 패키지를 쓴다면 위 줄 대신 이 줄을 사용

namespace DanawaR_Host
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// MainWindow.xaml에 대응되는 코드 비하인드 파일.
    /// UI 이벤트 처리 및 초기 설정, 대시보드 갱신 로직을 담당한다.
    /// </summary>
    public partial class MainWindow : Window
    {
        // 대시보드 ViewModel 인스턴스 (null 아님)
        private readonly DashboardViewModel _vm;

        // MainWindow 생성자 — 윈도우가 생성될 때 자동 실행됨.
        public MainWindow()
        {
            InitializeComponent();                 // XAML에 정의된 UI 요소들 초기화

            // ViewModel 생성 후 DataContext에 연결 (모든 바인딩의 기준 객체)
            _vm = new DashboardViewModel();
            this.DataContext = _vm;

            // 서버 상태 텍스트 초기값
            StatusText.Content = "서버 실행 중 (포트 9000)";

            // 창 로드 완료 후 첫 데이터 로딩
            this.Loaded += async (s, e) =>
            {
                StatusText.Content = "DB에서 초기 데이터 로딩 중...";
                await _vm.RefreshAsync();
                StatusText.Content = "실시간 모니터링 중";
            };
        }

        /// <summary>
        /// 종료 명령 버튼 클릭 시 호출되는 이벤트 핸들러.
        /// ★ 선택된 Agent 1대에게만 SHUTDOWN 명령을 전송한다.
        /// </summary>
        private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            // UI에 현재 상태 표시
            StatusText.Content = "종료 명령 전송 중...";

            try
            {
                // 선택된 Agent가 없으면 아무 것도 하지 않음
                if (_vm.SelectedAgent == null)
                {
                    StatusText.Content = "종료할 에이전트가 선택되지 않았습니다.";
                    return;
                }

                string targetDeviceId = _vm.SelectedAgent.DeviceID;

                // Server.cs에 구현할 단일 대상 종료 메서드 호출
                await Server.SendShutdownToAsync(targetDeviceId);

                StatusText.Content = $"종료 명령 전송 완료 (DeviceID={targetDeviceId})";
            }
            catch (Exception ex)
            {
                // 전송 중 오류가 난 경우 상태 및 디버그 로그 출력
                StatusText.Content = "종료 명령 전송 실패";
                System.Diagnostics.Debug.WriteLine("Shutdown error: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 각 Agent(PC) 한 대의 최신 상태를 나타내는 모델
    /// </summary>
    public class AgentSummary : INotifyPropertyChanged
    {
        public string DeviceID { get; set; } = "";
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Disk { get; set; }
        public double Temp { get; set; }
        public double NetSentKB { get; set; }
        public double NetRecvKB { get; set; }
        public DateTime LastTimestamp { get; set; }

        /// <summary>
        /// 최근 데이터가 일정 시간(예: 10초) 이내면 "가동중", 아니면 "정지됨"
        /// </summary>
        public bool IsRunning
        {
            get
            {
                var diff = DateTime.Now - LastTimestamp;
                return diff.TotalSeconds <= 10;   // 필요에 따라 시간 조정
            }
        }

        public string StatusText => IsRunning ? "가동중" : "정지됨";

        // 리스트에 보여줄 요약 라인
        public string SummaryLine1 => $"CPU: {Cpu:F1}% | RAM: {Ram:F1}%";
        public string SummaryLine2 => $"Disk: {Disk:F1}% | Temp: {Temp:F1}°C";

        // nullable 이벤트로 선언 (INotifyPropertyChanged와 일치)
        public event PropertyChangedEventHandler? PropertyChanged;

        public void NotifyAll()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    /// <summary>
    /// 메인 대시보드용 ViewModel
    ///   - Agents: 좌측 Agent 리스트
    ///   - SelectedAgent: 선택된 Agent
    ///   - CurrentCpu/Ram/...: 상세 패널 표시용 값
    ///   - CpuHistory: CPU 차트 데이터 (최근 N개)
    /// </summary>
    public class DashboardViewModel : INotifyPropertyChanged
    {
        // ===== DB 연결 문자열 =====
        private readonly string _conn =
            ConfigurationManager.ConnectionStrings["DB"].ConnectionString;

        // ===== 주기적 갱신 타이머 =====
        private readonly DispatcherTimer _timer;

        // ===== 좌측 Agent 리스트 =====
        public ObservableCollection<AgentSummary> Agents { get; } =
            new ObservableCollection<AgentSummary>();

        // null 허용(처음엔 선택된 Agent가 없을 수 있음)
        private AgentSummary? _selectedAgent;
        public AgentSummary? SelectedAgent
        {
            get => _selectedAgent;
            set
            {
                if (!ReferenceEquals(_selectedAgent, value))
                {
                    _selectedAgent = value;
                    OnPropertyChanged(nameof(SelectedAgent));
                    // 선택이 바뀔 때 해당 Agent 기준으로 상세/차트 갱신
                    _ = LoadDetailAsync();
                }
            }
        }

        // ===== 상세 패널용 현재 값 =====
        private double _currentCpu;
        public double CurrentCpu
        {
            get => _currentCpu;
            set { _currentCpu = value; OnPropertyChanged(nameof(CurrentCpu)); }
        }

        private double _currentRam;
        public double CurrentRam
        {
            get => _currentRam;
            set { _currentRam = value; OnPropertyChanged(nameof(CurrentRam)); }
        }

        private double _currentDisk;
        public double CurrentDisk
        {
            get => _currentDisk;
            set { _currentDisk = value; OnPropertyChanged(nameof(CurrentDisk)); }
        }

        private double _currentTemp;
        public double CurrentTemp
        {
            get => _currentTemp;
            set { _currentTemp = value; OnPropertyChanged(nameof(CurrentTemp)); }
        }

        private string _networkText = "송신: 0.0KB/s | 수신: 0.0KB/s";
        public string NetworkText
        {
            get => _networkText;
            set { _networkText = value; OnPropertyChanged(nameof(NetworkText)); }
        }

        // ===== CPU 차트 데이터 =====
        public LiveCharts.ChartValues<double> CpuHistory { get; } =
            new LiveCharts.ChartValues<double>();

        // X축에 쓸 타임스탬프 리스트 (CpuHistory와 인덱스 맞춤)
        private readonly System.Collections.Generic.List<DateTime> _timeStamps =
            new System.Collections.Generic.List<DateTime>();

        // LiveCharts X축 LabelFormatter
        public Func<double, string> TimeLabelFormatter { get; }

        public DashboardViewModel()
        {
            // LabelFormatter: X 값(인덱스)을 시간 문자열로 변환
            TimeLabelFormatter = index =>
            {
                int i = (int)Math.Round(index);
                if (i >= 0 && i < _timeStamps.Count)
                    return _timeStamps[i].ToString("HH:mm:ss");
                return "";
            };

            // 타이머 설정 (예: 2초마다 DB에서 최신값 조회)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += async (s, e) => await RefreshAsync();
            _timer.Start();
        }

        /// <summary>
        /// 전체 대시보드 갱신:
        ///   1) 각 DeviceID별 마지막 1건 가져와서 Agents 리스트 갱신
        ///   2) 선택된 Agent 기준으로 상세/차트 갱신
        /// </summary>
        public async Task RefreshAsync()
        {
            try
            {
                await LoadAgentsAsync();
                await LoadDetailAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] RefreshAsync error: " + ex.Message);
            }
        }

        /// <summary>
        /// SensorDataLog에서 각 DeviceID의 최신 1건만 가져와 Agents 컬렉션을 갱신
        /// </summary>
        private async Task LoadAgentsAsync()
        {
            var list = new System.Collections.Generic.List<AgentSummary>();

            using (var con = new MySqlConnection(_conn))
            {
                await con.OpenAsync();

                // 각 DeviceID별 가장 큰 LogID(=가장 최신)를 이용해 1건씩 가져옴
                string sql = @"
                    SELECT s.*
                    FROM SensorDataLog s
                    INNER JOIN (
                        SELECT DeviceID, MAX(LogID) AS MaxLogID
                        FROM SensorDataLog
                        GROUP BY DeviceID
                    ) t ON s.DeviceID = t.DeviceID AND s.LogID = t.MaxLogID;
                ";

                using (var cmd = new MySqlCommand(sql, con))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var a = new AgentSummary
                        {
                            DeviceID = reader.GetString("DeviceID"),
                            Cpu = reader.GetDouble("CpuUsage"),
                            Ram = reader.GetDouble("RamUsagePercent"),
                            Disk = reader.GetDouble("DiskUsagePercent"),
                            Temp = reader.GetDouble("VirtualTemp"),
                            NetSentKB = reader.GetDouble("NetworkSent") / 1024.0,
                            NetRecvKB = reader.GetDouble("NetworkReceived") / 1024.0,
                            LastTimestamp = reader.GetDateTime("Timestamp")
                        };
                        list.Add(a);
                    }
                }
            }

            // === '중지된' 에이전트는 목록에서 제외 (IsRunning=false 필터링) ===
            list = list
                .Where(a => a.IsRunning)
                .ToList();

            // ObservableCollection 갱신
            Application.Current.Dispatcher.Invoke(() =>
            {
                // ★★★ 선택 유지용으로 현재 선택된 DeviceID를 먼저 저장해 둔다
                //     (Agents.Clear() 하면서 ListBox 선택이 null로 바뀌는 문제 방지)
                string? selectedDeviceId = SelectedAgent?.DeviceID;

                Agents.Clear();
                foreach (var a in list.OrderBy(x => x.DeviceID))
                {
                    Agents.Add(a);
                }

                // 선택된 Agent가 없거나, 기존 선택이 목록에 없으면 첫 번째를 선택
                if (Agents.Count > 0)
                {
                    if (string.IsNullOrEmpty(selectedDeviceId))
                    {
                        // 이전에 선택이 없었으면 첫 번째 항목 선택
                        SelectedAgent = Agents[0];
                    }
                    else
                    {
                        // 저장해둔 DeviceID와 같은 항목을 다시 찾아서 선택
                        var same = Agents.FirstOrDefault(x => x.DeviceID == selectedDeviceId);
                        if (same == null)
                            SelectedAgent = Agents[0];   // 해당 DeviceID가 사라졌으면 첫 번째로
                        else
                            SelectedAgent = same;         // 같은 DeviceID를 가진 새 객체로 다시 선택
                    }
                }
                else
                {
                    // 모든 Agent가 중지되어 목록이 비었을 때는 선택된 Agent도 없애서
                    // 오른쪽 상세 패널/차트 조회가 계속 실행되지 않도록 처리
                    SelectedAgent = null;
                }
            });
        }

        /// <summary>
        /// SelectedAgent 기준으로
        ///   - 최근 N개의 SensorDataLog를 읽어와 차트/상세 정보를 채움
        /// </summary>
        private async Task LoadDetailAsync()
        {
            if (SelectedAgent == null) return;

            const int HISTORY_COUNT = 30; // 차트에 표시할 포인트 개수

            using (var con = new MySqlConnection(_conn))
            {
                await con.OpenAsync();

                string sql = @"
                    SELECT *
                    FROM SensorDataLog
                    WHERE DeviceID = @DeviceID
                    ORDER BY Timestamp DESC
                    LIMIT @Limit;
                ";

                using (var cmd = new MySqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@DeviceID", SelectedAgent.DeviceID);
                    cmd.Parameters.AddWithValue("@Limit", HISTORY_COUNT);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        var list = new System.Collections.Generic.List<(DateTime ts, double cpu, double ram, double disk, double temp, double nSentKB, double nRecvKB)>();

                        while (await reader.ReadAsync())
                        {
                            var ts = reader.GetDateTime("Timestamp");
                            double cpu = reader.GetDouble("CpuUsage");
                            double ram = reader.GetDouble("RamUsagePercent");
                            double disk = reader.GetDouble("DiskUsagePercent");
                            double temp = reader.GetDouble("VirtualTemp");
                            double nSentKB = reader.GetDouble("NetworkSent") / 1024.0;
                            double nRecvKB = reader.GetDouble("NetworkReceived") / 1024.0;

                            list.Add((ts, cpu, ram, disk, temp, nSentKB, nRecvKB));
                        }

                        // 최신순 DESC로 읽었으니, 차트는 시간 순서대로 보여주기 위해 역순 정렬
                        list.Sort((a, b) => a.ts.CompareTo(b.ts));

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // CPU 히스토리 / 타임스탬프 갱신
                            CpuHistory.Clear();
                            _timeStamps.Clear();

                            foreach (var item in list)
                            {
                                CpuHistory.Add(item.cpu);
                                _timeStamps.Add(item.ts);
                            }

                            // 가장 마지막(가장 최근) 데이터 기준으로 상세 값 갱신
                            if (list.Count > 0)
                            {
                                var last = list[list.Count - 1];

                                CurrentCpu = last.cpu;
                                CurrentRam = last.ram;
                                CurrentDisk = last.disk;
                                CurrentTemp = last.temp;
                                NetworkText = $"송신: {last.nSentKB:F1}KB/s | 수신: {last.nRecvKB:F1}KB/s";
                            }
                        });
                    }
                }
            }
        }

        // ===== INotifyPropertyChanged 구현 =====
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
