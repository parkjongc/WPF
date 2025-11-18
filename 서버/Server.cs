using System;
using System.Collections.Generic;
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
                lock (clients) clients.Remove(client);
                client.Close();
                Debug.WriteLine("[HOST] Client disconnected");
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
            Debug.WriteLine("[HOST] DB Insert OK");
        }

        private static Task SendJsonAsync(TcpClient client, object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            byte[] data = Encoding.UTF8.GetBytes(json);
            var stream = client.GetStream();

            return stream.WriteAsync(data, 0, data.Length);
        }

        private static string GenerateTempCode()
        {
            lock (rand)
            {
                return rand.Next(100000, 999999).ToString();
            }
        }

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