using Dapper;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace DatabaseLogger
{
    /// <summary>
    /// [F] DB 담당자가 작성할 데이터베이스 로깅 전용 클래스입니다.
    /// Dapper를 사용하여 SQL Server에 비동기로 데이터를 INSERT합니다.
    /// (Disk, Network 필드 추가)
    /// </summary>
    public class Logger
    {
        private readonly string _connectionString;

        /// <summary>
        /// Host 앱이 시작될 때 DB 연결 문자열을 받아 초기화합니다.
        /// </summary>
        public Logger(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// [A] 서버 담당자가 Agent로부터 JSON을 수신할 때 호출할 메서드입니다.
        /// </summary>
        public async Task LogDataAsync(string sensorJson)
        {
            // 1. Agent가 보낸 JSON을 C# 객체로 역직렬화합니다.
            var data = JsonConvert.DeserializeObject<SensorDataDto>(sensorJson);

            // 2. Dapper를 사용한 SQL INSERT 쿼리 (Disk, Network 컬럼 추가)
            const string sql = @"
                INSERT INTO SensorDataLog (
                    DeviceID, CpuUsage, RamUsagePercent, DiskUsagePercent, 
                    NetworkSent, NetworkReceived, VirtualTemp
                )
                VALUES (
                    @DeviceID, @CpuUsage, @RamUsagePercent, @DiskUsagePercent, 
                    @NetworkSent, @NetworkReceived, @VirtualTemp
                );";
            
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    // 비동기로 Dapper 쿼리 실행
                    await connection.ExecuteAsync(sql, data);
                }
            }
            catch (SqlException ex)
            {
                // 실제 환경에서는 이 로그를 Host UI나 파일로 기록해야 합니다.
                System.Diagnostics.Debug.WriteLine($"[DB Error] Sensor Log 실패: {ex.Message}");
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JSON Error] Sensor JSON 파싱 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// [A] 서버 담당자가 Dashboard(Host UI)로부터 제어 명령을 받을 때 호출할 메서드입니다.
        /// </summary>
        public async Task LogAuditAsync(string commandJson)
        {
            // 1. 제어 명령 JSON을 C# 객체로 역직렬화합니다.
            var command = JsonConvert.DeserializeObject<AuditCommandDto>(commandJson);

            // 2. Dapper를 사용한 SQL INSERT 쿼리
            const string sql = @"
                INSERT INTO AuditLog (UserID, TargetDeviceID, Command, CommandValue)
                VALUES (@UserID, @TargetDeviceID, @Command, @CommandValue);";

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    // MVP에서는 UserID를 "Host_User" 등으로 고정합니다.
                    var param = new
                    {
                        UserID = "Host_User", // MVP 고정값
                        command.TargetDeviceID,
                        command.Command,
                        command.CommandValue
                    };
                    
                    // 비동기로 Dapper 쿼리 실행
                    await connection.ExecuteAsync(sql, param);
                }
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB Error] Audit Log 실패: {ex.Message}");
            }
             catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JSON Error] Command JSON 파싱 실패: {ex.Message}");
            }
        }
    }

    // --- JSON 역직렬화를 위한 DTO (Data Transfer Object) 클래스 ---

    /// <summary>
    /// Agent가 1초마다 보낼 데이터 JSON 구조
    /// (C# Agent 로직과 동일해야 함)
    /// (Disk, Network 필드 추가)
    /// </summary>
    internal class SensorDataDto
    {
        public string DeviceID { get; set; }
        public float CpuUsage { get; set; }
        public float RamUsagePercent { get; set; }
        public float DiskUsagePercent { get; set; }  // Disk (%)
        public float NetworkSent { get; set; }      // Network Sent (Bytes/sec)
        public float NetworkReceived { get; set; }  // Network Received (Bytes/sec)
        public float VirtualTemp { get; set; }
    }

    /// <summary>
    /// Host UI(Dashboard)가 제어 시 생성할 명령 JSON 구조
    /// (C# Dashboard 로직과 동일해야 함)
    /// </summary>
    internal class AuditCommandDto
    {
        public string TargetDeviceID { get; set; }
        public string Command { get; set; }
        public string CommandValue { get; set; } // 예: "SET_SP"의 "85.0"
    }
}