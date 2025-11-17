using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DanawaRClient
{
    public class SensorData
    {
        public string DeviceID { get; set; }
        public double CpuUsage { get; set; }
        public double RamUsagePercent { get; set; }
        public double DiskUsagePercent { get; set; }
        public double NetworkSent { get; set; }
        public double NetworkReceived { get; set; }
        public double VirtualTemp { get; set; }
    }

    public class DataSender : IDisposable
    {
        private readonly string _serverHost;
        private readonly int _serverPort;
        private readonly string _deviceId;

        public DataSender(string serverUrl, string deviceId)
        {
            // URL에서 호스트와 포트 추출
            // 예: "http://localhost:5000/api/sensor" -> localhost, 5000
            var uri = new Uri(serverUrl);
            _serverHost = uri.Host;
            _serverPort = uri.Port;
            _deviceId = deviceId;

            Debug.WriteLine($"[DataSender] 초기화 완료 - Device: {_deviceId}, Server: {_serverHost}:{_serverPort}");
        }

        public async Task SendSensorDataAsync(SensorData data)
        {
            TcpClient client = null;

            try
            {
                data.DeviceID = _deviceId;

                // JSON 직렬화
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                // TCP 클라이언트 생성 및 연결
                client = new TcpClient();
                await client.ConnectAsync(_serverHost, _serverPort);

                // JSON 데이터를 바이트로 변환
                var jsonBytes = Encoding.UTF8.GetBytes(json);

                // 개행 문자 추가 (서버가 라인 단위로 읽는 경우)
                var dataToSend = Encoding.UTF8.GetBytes(json + "\n");

                // 네트워크 스트림 가져오기
                NetworkStream stream = client.GetStream();

                // 데이터 전송
                await stream.WriteAsync(dataToSend, 0, dataToSend.Length);
                await stream.FlushAsync();

                Debug.WriteLine($"[DataSender] TCP 데이터 전송 성공: {_deviceId} -> {_serverHost}:{_serverPort}");
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"[DataSender] 소켓 오류: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DataSender] 오류: {ex.Message}");
            }
            finally
            {
                // 연결 종료
                client?.Close();
            }
        }

        public void Dispose()
        {
            // TCP는 매번 새로 연결하므로 특별히 정리할 것 없음
        }
    }
}