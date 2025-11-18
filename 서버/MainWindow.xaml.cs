using System;                      // 기본 시스템 기능(예: EventArgs, 예외 처리 등)
using System.Text;                 // 문자열 인코딩/디코딩을 위한 네임스페이스
using System.Threading.Tasks;      // async/await 사용을 위한 네임스페이스
using System.Windows;              // WPF 기본 Window, Application 클래스 포함
using System.Windows.Controls;     // 버튼, 텍스트박스 등 UI 컨트롤 포함
using System.Windows.Data;         // 데이터 바인딩 관련 기능
using System.Windows.Documents;    // 문서 관련 (RichText, FlowDocument 등)
using System.Windows.Input;        // 키보드/마우스 입력 처리
using System.Windows.Media;        // UI 색상, 브러시, 그리기 기능
using System.Windows.Media.Imaging; // 이미지 처리 기능
using System.Windows.Navigation;    // WPF 내비게이션 기능
using System.Windows.Shapes;        // 도형(사각형, 원 등) 그리기 기능

namespace DanawaR_Host
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// MainWindow.xaml에 대응되는 코드 비하인드 파일.
    /// UI 이벤트 처리 및 초기 설정 코드를 담당한다.
    /// </summary>
    public partial class MainWindow : Window
    {
        // MainWindow 생성자 — 윈도우가 생성될 때 자동 실행됨.
        public MainWindow()
        {
            InitializeComponent();
            // XAML에 정의된 UI 요소들을 실제 객체로 초기화하는 필수 메서드.
            // 이걸 호출하지 않으면 XAML UI가 화면에 표시되지 않음.

            StatusText.Content = "서버 실행 중 (포트 9000)";
            // XAML에 배치된 TextBlock(이름: StatusText)에
            // 초기 메시지를 설정해주는 부분.
            // 프로그램 실행 직후 UI에 서버 상태를 보여주기 위해 사용함.
        }

        /// <summary>
        /// 종료 명령 버튼 클릭 시 호출되는 이벤트 핸들러.
        /// 접속 중인 모든 클라이언트에게 JSON 기반 SHUTDOWN 명령을 전송한다.
        /// </summary>
        private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            // UI에 현재 상태 표시
            StatusText.Content = "종료 명령 전송 중...";

            try
            {
                // Server.cs에 구현된 BroadcastShutdownAsync 호출
                await Server.BroadcastShutdownAsync();

                // 전송 완료 후 상태 갱신
                StatusText.Content = "종료 명령 전송 완료";
            }
            catch (Exception ex)
            {
                // 전송 중 오류가 난 경우 상태 및 디버그 로그 출력
                StatusText.Content = "종료 명령 전송 실패";
                System.Diagnostics.Debug.WriteLine("Shutdown error: " + ex.Message);
            }
        }
    }
}
