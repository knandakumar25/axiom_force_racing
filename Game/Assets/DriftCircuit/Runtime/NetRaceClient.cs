using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DriftCircuit
{
    internal struct RemotePeerState
    {
        public int PeerId;
        public string DriverId;
        public Vector3 Position;
        public float RotationY;
        public float Speed;
        public int Lap;
        public int Gate;
    }

    /// <summary>Connects to the Express WebSocket race relay (`/ws/race`) for online multiplayer rooms.</summary>
    internal sealed class NetRaceClient : IDisposable
    {
        [Serializable]
        private sealed class WireMessage
        {
            public string type;
            public string code;
            public string circuit;
            public string driverId;
            public string error;
            public int peerId;
            public float x, y, z, ry, speed;
            public int lap, gate;
            public WirePeer[] peers;
        }

        [Serializable]
        private sealed class WirePeer
        {
            public int id;
            public string name;
            public string driverId;
        }

        private readonly Uri relayUri;
        private readonly ConcurrentQueue<WireMessage> incoming = new();
        private readonly Dictionary<int, RemotePeerState> remotes = new();
        private ClientWebSocket socket;
        private CancellationTokenSource cts;

        public bool IsConnected => socket?.State == WebSocketState.Open;
        public string RoomCode { get; private set; }
        public string RoomCircuit { get; private set; }
        public int LocalPeerId { get; private set; }
        public string StatusText { get; private set; } = "Online: Disconnected";
        public int PeerCount { get; private set; }
        public bool RaceStarted { get; private set; }
        public IReadOnlyDictionary<int, RemotePeerState> Remotes => remotes;

        public NetRaceClient(string wsUrl = null)
        {
            string url = wsUrl ?? Environment.GetEnvironmentVariable("DRIFT_CIRCUIT_WS_URL")
                ?? "ws://localhost:8001/ws/race";
            relayUri = new Uri(url);
        }

        public async Task<bool> HostRoomAsync(string circuitId, string driverId, string name)
        {
            if (!await EnsureConnectedAsync())
                return false;
            await SendJsonAsync($"{{\"type\":\"create\",\"circuit\":\"{Escape(circuitId)}\",\"driverId\":\"{Escape(driverId)}\",\"name\":\"{Escape(name)}\"}}");
            return true;
        }

        public async Task<bool> JoinRoomAsync(string code, string driverId, string name)
        {
            if (string.IsNullOrWhiteSpace(code) || !await EnsureConnectedAsync())
                return false;
            await SendJsonAsync($"{{\"type\":\"join\",\"code\":\"{Escape(code.Trim().ToUpperInvariant())}\",\"driverId\":\"{Escape(driverId)}\",\"name\":\"{Escape(name)}\"}}");
            return true;
        }

        public Task StartRaceAsync() =>
            IsConnected ? SendJsonAsync("{\"type\":\"start\"}") : Task.CompletedTask;

        public Task SendStateAsync(Vector3 pos, float rotY, float speed, int lap, int gate, string driverId)
        {
            if (!IsConnected || string.IsNullOrEmpty(RoomCode))
                return Task.CompletedTask;
            string msg = FormattableString.Invariant(
                $"{{\"type\":\"state\",\"x\":{pos.x:F2},\"y\":{pos.y:F2},\"z\":{pos.z:F2},\"ry\":{rotY:F1},\"speed\":{speed:F2},\"lap\":{lap},\"gate\":{gate},\"driverId\":\"{Escape(driverId)}\"}}");
            return SendJsonAsync(msg);
        }

        public void DrainUpdates()
        {
            while (incoming.TryDequeue(out WireMessage msg))
            {
                switch (msg.type)
                {
                    case "created":
                    case "joined":
                        RoomCode = msg.code;
                        RoomCircuit = msg.circuit;
                        LocalPeerId = msg.peerId;
                        PeerCount = msg.peers?.Length ?? 1;
                        RaceStarted = false;
                        StatusText = $"Room {RoomCode} ({PeerCount} racer{(PeerCount == 1 ? "" : "s")})";
                        break;
                    case "peer_joined":
                        PeerCount++;
                        StatusText = $"Room {RoomCode} ({PeerCount} racers)";
                        break;
                    case "peer_left":
                        PeerCount = Math.Max(1, PeerCount - 1);
                        remotes.Remove(msg.peerId);
                        StatusText = $"Room {RoomCode} ({PeerCount} racer{(PeerCount == 1 ? "" : "s")})";
                        break;
                    case "started":
                        RoomCircuit = msg.circuit;
                        RaceStarted = true;
                        StatusText = $"Room {RoomCode} Racing";
                        break;
                    case "state":
                        if (msg.peerId != LocalPeerId)
                        {
                            remotes[msg.peerId] = new RemotePeerState
                            {
                                PeerId = msg.peerId,
                                DriverId = msg.driverId,
                                Position = new Vector3(msg.x, msg.y, msg.z),
                                RotationY = msg.ry,
                                Speed = msg.speed,
                                Lap = msg.lap,
                                Gate = msg.gate
                            };
                        }
                        break;
                    case "error":
                        StatusText = $"Online error: {msg.error}";
                        break;
                }
            }
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (IsConnected)
                return true;
            try
            {
                DisconnectSync();
                cts = new CancellationTokenSource();
                socket = new ClientWebSocket();
                await socket.ConnectAsync(relayUri, cts.Token);
                StatusText = "Online: Connected";
                _ = ReceiveLoopAsync(socket, cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"Online unavailable ({ex.GetType().Name})";
                return false;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            var sb = new StringBuilder();
            try
            {
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        WireMessage msg = JsonUtility.FromJson<WireMessage>(sb.ToString());
                        sb.Clear();
                        if (msg != null && !string.IsNullOrEmpty(msg.type))
                            incoming.Enqueue(msg);
                    }
                }
            }
            catch { }
        }

        private async Task SendJsonAsync(string json)
        {
            if (!IsConnected) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }

        private static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void DisconnectSync()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = null;
            socket?.Dispose();
            socket = null;
            RoomCode = null;
            remotes.Clear();
        }

        public void Dispose() => DisconnectSync();
    }
}
