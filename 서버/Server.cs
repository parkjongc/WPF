using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;          // ConfigurationManager
using MySqlConnector;                // MySQL 전용 (SqlClient 대신)
using System.Diagnostics;           // Debug.WriteLine
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanawaR_Host
{
    public static class Server
    {
        private static TcpListener? listener;
        private static readonly List<TcpClient> clients = new();
        // DeviceID별로 클라이언트 매핑 (선택된 PC만 종료 시 사용)
        private static readonly Dictionary<string, TcpClient> deviceMap = new();
        private const int Port = 9000;

        private static readonly Random rand = new();

        // App.config의 connectionStrings["DB"] 사용
        private static readonly string conn =
            ConfigurationManager.ConnectionStrings["DB"].ConnectionString;

        public static void Start()
        {
            if (listener != null) return;

            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Debug.WriteLine($"[HOST] Server started on port {Port}");

            _ = AcceptLoop();
        }

        private static async Task AcceptLoop()
        {
            if (listener == null) return;

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                lock (clients) clients.Add(client);
                Debug.WriteLine("[HOST] Client connected");

                _ = HandleClient(client);

                string code = GenerateTempCode();
                var authMsg = new AuthMessage
                {
                    type = "AUTH_CODE",
                    code = code,
                    expireSec = 300
                };

                await SendJsonAsync(client, authMsg);
                Debug.WriteLine($"[HOST] Sent AUTH_CODE: {code}");
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];

            try
            {
                while (client.Connected)
                {
                    int bytes = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytes <= 0) break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, bytes);
                    Debug.WriteLine("[HOST] Received: " + msg);

                    SensorData? data = null;
                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        data = JsonSerializer.Deserialize<SensorData>(msg, options);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[HOST] JSON parse error: " + ex.Message);
                    }

                    if (data != null && !string.IsNullOrEmpty(data.DeviceID))
                    {
                        // 이 클라이언트가 보고한 DeviceID 기준으로 매핑 등록/갱신
                        lock (deviceMap)
                        {
                            deviceMap[data.DeviceID] = client;
                        }

                        try
                        {
                            await SaveToDBAsync(data);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[HOST] DB insert error: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HOST] HandleClient error: " + ex.Message);
            }
            finally
            {
                // 연결 종료된 클라이언트 제거
                lock (clients) clients.Remove(client);

                // deviceMap에서 이 클라이언트를 사용하는 모든 DeviceID 제거
                lock (deviceMap)
                {
                    var removeKeys = new List<string>();
                    foreach (var kv in deviceMap)
                    {
                        if (kv.Value == client)
                            removeKeys.Add(kv.Key);
                    }
                    foreach (var key in removeKeys)
                        deviceMap.Remove(key);
                }

                client.Close();
                Debug.WriteLine("[HOST] Client disconnected");
            }
        }

        private static async Task SendJsonAsync(TcpClient client, object obj)
        {
            try
            {
                var stream = client.GetStream();
                string json = JsonSerializer.Serialize(obj);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HOST] SendJson error: " + ex.Message);
            }
        }

        // MySQL용으로 변경 + async
        private static async Task SaveToDBAsync(SensorData d)
        {
            using var con = new MySqlConnection(conn);
            await con.OpenAsync();

            string q = @"
                INSERT INTO SensorDataLog
                (DeviceID, CpuUsage, RamUsagePercent, DiskUsagePercent,
                 NetworkSent, NetworkReceived, VirtualTemp)
                VALUES
                (@DeviceID, @Cpu, @Ram, @Disk, @NSent, @NRecv, @Temp);
            ";

            using var cmd = new MySqlCommand(q, con);
            cmd.Parameters.AddWithValue("@DeviceID", d.DeviceID);
            cmd.Parameters.AddWithValue("@Cpu", d.CpuUsage);
            cmd.Parameters.AddWithValue("@Ram", d.RamUsagePercent);
            cmd.Parameters.AddWithValue("@Disk", d.DiskUsagePercent);
            cmd.Parameters.AddWithValue("@NSent", d.NetworkSent);
            cmd.Parameters.AddWithValue("@NRecv", d.NetworkReceived);
            cmd.Parameters.AddWithValue("@Temp", d.VirtualTemp);

            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[HOST] DB insert OK");
        }

        private static string GenerateTempCode()
        {
            lock (rand)
            {
                return rand.Next(100000, 999999).ToString();
            }
        }

        // 특정 DeviceID 한 대만 종료 명령 전송
        public static async Task SendShutdownToAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Debug.WriteLine("[HOST] SendShutdownToAsync: deviceId is null or empty");
                return;
            }

            TcpClient? target = null;
            lock (deviceMap)
            {
                if (deviceMap.TryGetValue(deviceId, out var client))
                {
                    target = client;
                }
            }

            if (target == null || !target.Connected)
            {
                Debug.WriteLine($"[HOST] SendShutdownToAsync: target not found or not connected (DeviceID={deviceId})");
                return;
            }

            var shutdownMsg = new { type = "SHUTDOWN" };
            await SendJsonAsync(target, shutdownMsg);
            Debug.WriteLine($"[HOST] SHUTDOWN sent to DeviceID={deviceId}");
        }

        // 기존: 전체 브로드캐스트 종료 (원하면 버튼 따로 만들어서 사용)
        public static async Task BroadcastShutdownAsync()
        {
            var shutdownMsg = new { type = "SHUTDOWN" };

            List<TcpClient> snapshot;
            lock (clients) snapshot = new List<TcpClient>(clients);

            foreach (var c in snapshot)
            {
                if (!c.Connected) continue;
                await SendJsonAsync(c, shutdownMsg);
            }

            Debug.WriteLine("[HOST] Broadcast SHUTDOWN");
        }
    }

    public class AuthMessage
    {
        public string type { get; set; } = "AUTH_CODE";
        public string code { get; set; } = "";
        public int expireSec { get; set; } = 300;
    }

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
