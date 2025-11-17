using System;
using System.Net.Http;
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
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl;
        private readonly string _deviceId;

        public DataSender(string serverUrl, string deviceId = "Agent-01")
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            _serverUrl = serverUrl;
            _deviceId = deviceId;
        }

        public async Task SendSensorDataAsync(SensorData data)
        {
            try
            {
                data.DeviceID = _deviceId;

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_serverUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[DataSender] 데이터 전송 성공: {_deviceId}");
                }
                else
                {
                    Debug.WriteLine($"[DataSender] 전송 실패: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[DataSender] 네트워크 오류: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"[DataSender] 타임아웃 발생");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DataSender] 오류: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}