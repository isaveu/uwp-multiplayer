using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;
using static Game.Events;
using static Game.MainPage;

namespace Game
{
    public struct Packet
    {
        public short commandType, payloadSize;
        public byte[] payload;

        public Packet(short commandType, short payloadSize, byte[] payload)
        {
            this.commandType = commandType;
            this.payloadSize = payloadSize;
            this.payload = payload;
        }
    }

    public class Connection
    {
        private const uint PacketHeaderSize = 4;
        private const uint MaxPingTime = 10 * 1000;

        public StreamSocket StreamSocket { get; set; }

        public bool Host { get; set; }
        public string PlayerId { get; set; }
        public bool Joined { get; set; }
        public Stopwatch PingTimer { get; set; }
        public ulong PingTime { get; set; }

        public bool Ready { get; set; }
        public bool MapLoaded { get; set; }
        public bool MapShown { get; set; }

        public float PlayerX { get; set; }
        public float PlayerY { get; set; }
        public float PlayerAngle { get; set; }
        public ushort TargetIndex { get; set; }

        public bool LeaveRequestSent { get; set; }
        public bool LeaveRequestAccepted { get; set; }
        public bool CleanupCompleted { get; set; }

        private readonly IInputStream inputStream = null;
        private readonly IOutputStream outputStream = null;

        private DataReader dataReader = null;
        private DataWriter dataWriter = null;

        private CancellationTokenSource cancellationTokenSource = null;

        private Guid id = Guid.NewGuid();
        private Server server = null;
        private readonly bool host = false;
        private bool connected = true;

        private Timer pingInterval = null;
        private bool pingReplyReceived = true;

        private bool leaveRequestReceived = false;

        public event PingTestReplyHandler PingTestReplyReceivedEvent;
        public event JoinMatchHandler JoinMatchEvent;
        public event PlayerJoinedHandler PlayerJoinedEvent;
        public event PlayerLeftHandler PlayerLeftEvent;
        public event MatchStateHandler MatchStateEvent;
        public event ReadyTimeHandler ReadyTimeEvent;
        public event PlayerPingHandler PlayerPingEvent;
        public event PlayerReadyHandler PlayerReadyEvent;
        public event LoadMapHandler LoadMapEvent;
        public event MapObjectsCreatedHandler MapObjectsCreatedEvent;
        public event PlayerMapObjectsRequestedHandler PlayerMapObjectsRequestedEvent;
        public event MapDataEndHandler MapDataEndEvent;
        public event PlayerPositionHandler PlayerPositionEvent;
        public event TargetPositionHandler TargetPositionEvent;
        public event PlayerScoreHandler PlayerScoreEvent;
        public event ShowMapHandler ShowMapEvent;
        public event MatchCountdownHandler MatchCountdownEvent;
        public event MatchStartedHandler MatchStartedEvent;
        public event MatchTimeLeftHandler MatchTimeLeftEvent;
        public event PlayerTargetReachedHandler PlayerTargetReachedEvent;
        public event MatchEndedHandler MatchEndedEvent;
        public event LeaveReplyHandler LeaveReplyEvent;
        public event LoadMatchDetailsHandler LoadMatchDetailsEvent;
        public event ShowMatchDetailsHandler ShowMatchDetailsEvent;

        public Connection(Server server, StreamSocket streamSocket)
        {
            Game.Log("Connection created (" + id + ").");

            this.server = server;

            if (server != null)
            {
                host = true;
            }

            StreamSocket = streamSocket;

            inputStream = StreamSocket.InputStream;
            outputStream = StreamSocket.OutputStream;

            dataReader = new DataReader(inputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial,
                ByteOrder = ByteOrder.LittleEndian
            };

            dataWriter = new DataWriter(outputStream)
            {
                ByteOrder = ByteOrder.LittleEndian,
                UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8
            };

            cancellationTokenSource = new CancellationTokenSource();

            ParsePackets().ContinueWith((task) =>
            {
                if (task.IsFaulted)
                {
                    if (task.Exception.GetType() != typeof(TaskCanceledException))
                    {
                        Game.Log(task.Exception.Message);

                        if (dataReader != null)
                        {
                            dataReader.Dispose();
                        }

                        if (dataWriter != null)
                        {
                            dataWriter.Dispose();
                        }

                        if (StreamSocket != null)
                        {
                            StreamSocket.Dispose();
                        }
                    }
                }

                if (task.IsCompleted)
                {
                    if (connected)
                    {
                        connected = false;
                        Game.Log("Connection lost (" + id + ").");
                    }
                    else
                    {
                        Game.Log("Connection closed (" + id + ").");
                    }

                    HandleDisconnection();
                }
            });
        }

        public void Close()
        {
            // Stop ping interval
            if (pingInterval != null)
            {
                pingInterval.Dispose();
            }

            // Cancel read operation
            cancellationTokenSource.Cancel();

            // Stop loop
            connected = false;

            // Dispose everything
            if (dataReader != null)
            {
                dataReader.Dispose();
            }

            if (dataWriter != null)
            {
                dataWriter.Dispose();
            }

            if (StreamSocket != null)
            {
                StreamSocket.Dispose();
            }
        }

        public async Task ParsePackets()
        {
            short command = 0;
            short payloadSize = 0;
            byte[] payload = null;

            bool packetHeaderRead = false;

            while (connected)
            {
                if (!packetHeaderRead)
                {
                    if (dataReader.UnconsumedBufferLength < PacketHeaderSize)
                    {
                        await dataReader.LoadAsync(PacketHeaderSize - dataReader.UnconsumedBufferLength).AsTask(cancellationTokenSource.Token);
                    }
                    else
                    {
                        command = dataReader.ReadInt16();
                        payloadSize = dataReader.ReadInt16();
                        packetHeaderRead = true;
                    }
                }
                else
                {
                    if (payloadSize > 0 && dataReader.UnconsumedBufferLength < payloadSize)
                    {
                        await dataReader.LoadAsync((uint)payloadSize - dataReader.UnconsumedBufferLength).AsTask(cancellationTokenSource.Token);
                    }  
                    else if (payloadSize == 0 || dataReader.UnconsumedBufferLength >= payloadSize)
                    {
                        if (payloadSize > 0)
                        {
                            payload = new byte[payloadSize];
                            dataReader.ReadBytes(payload);
                        }

                        Packet packet = new Packet(command, payloadSize, payload);
                        ParsePacket(packet);

                        packetHeaderRead = false;
                    }
                }
            }
        }

        private async void HandleDisconnection()
        {
            if (pingInterval != null)
            {
                pingInterval.Dispose();
            }

            if ((!LeaveRequestSent && !leaveRequestReceived) && Joined)
            {
                if (host)
                {
                    // Broadcast to all players
                    byte[] playerLeftPayload = Encoding.ASCII.GetBytes(PlayerId);
                    Tuple<short, byte[]> playerLeftPacket = server.CreatePacket(Packets.PlayerLeft, playerLeftPayload);
                    await server.SendPacket(playerLeftPacket);
                }

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerLeftEvent?.Invoke(this, new PlayerLeftArguments(PlayerId, true));
                });
            }
        }

        private async void ParsePacket(Packet packet)
        {
            if (packet.payload == null)
            {
                Game.Log("Packet Received (" + packet.commandType + ").");
            }
            else if (packet.commandType == Packets.PingRequest && packet.commandType == Packets.PingReply && packet.commandType == Packets.PlayerPing &&
                     packet.commandType != Packets.Position && packet.commandType != Packets.PlayerPosition)
            {
                Game.Log("Packet Received (" + packet.commandType + ") " + ByteArray2String(packet.payload) + ".");
            }

            if (packet.commandType == 0 && host)
            {
                // <Host>
                // Ping request from player
                Game.Log("Ping test request received.");

                Tuple<short, byte[]> pingReplyPacket = CreatePacket(Packets.PingTestReply, null);
                await SendPacket(pingReplyPacket);

                this.Close();
            }
            else if (packet.commandType == 1)
            {
                // <Player>
                // Ping reply from host

                PingTimer.Stop();
                connected = false;

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PingTestReplyReceivedEvent?.Invoke(null, new PingTestReplyArguments(
                        StreamSocket.Information.RemoteAddress.ToString(),
                        Int32.Parse(StreamSocket.Information.RemotePort),
                        PingTimer.ElapsedMilliseconds
                    ));
                });
            }
            else if (packet.commandType == 2 && host)
            {
                // <Host>
                // Join request from player

                byte[] playerIdBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, packet.payload.Length);
                PlayerId = Encoding.UTF8.GetString(playerIdBytes);

                Game.Log("Join request from player (" + PlayerId + ").");

                // Code
                ushort code = 0;

                // Check if player id
                // is used
                if (Game.PlayerId == PlayerId)
                {
                    code = 1;
                }
                else
                {
                    for (int i = 0; i < server.Connections.Count; i++)
                    {
                        Connection connection = server.Connections[i];

                        if (connection.PlayerId == PlayerId)
                        {
                            code = 1;
                        }
                    }
                }

                if (code == 1)
                {
                    Game.Log("Join request from player (" + PlayerId + ") rejected, player id used.");
                }

                // Check if maximum number of players
                // is exceeded
                if (server.Connections.Count == 16)
                {
                    code = 2;
                    Game.Log("Join request from player (" + PlayerId + ") rejected, no slots available.");
                }

                // Check if host
                // left
                if (Game.State == States.Started)
                {
                    code = 3;
                    Game.Log("Join request from player (" + PlayerId + ") rejected, match is deleted.");
                }

                // Send reply
                byte[] joinReplyPayload = new byte[19];
                byte[] hostPlayerIdPaddedBytes = new byte[15];

                byte[] codeBytes = BitConverter.GetBytes(code);

                byte[] hostPlayerIdBytes = Encoding.ASCII.GetBytes(Game.PlayerId);
                System.Buffer.BlockCopy(hostPlayerIdBytes, 0, hostPlayerIdPaddedBytes, 0, hostPlayerIdBytes.Length);

                ushort hostReadyStatus = 0;

                if (Game.Ready)
                {
                    hostReadyStatus = 1;
                }

                byte[] readyStatusBytes = BitConverter.GetBytes(hostReadyStatus);

                System.Buffer.BlockCopy(codeBytes, 0, joinReplyPayload, 0, codeBytes.Length);
                System.Buffer.BlockCopy(hostPlayerIdPaddedBytes, 0, joinReplyPayload, codeBytes.Length, hostPlayerIdPaddedBytes.Length);
                System.Buffer.BlockCopy(readyStatusBytes, 0, joinReplyPayload,
                    codeBytes.Length + hostPlayerIdPaddedBytes.Length,
                    readyStatusBytes.Length);

                Tuple<short, byte[]> joinReplyPacket = CreatePacket(Packets.JoinReply, joinReplyPayload);
                await SendPacket(joinReplyPacket);

                if (code > 0)
                {
                    this.Close();
                    return;
                }

                if (Game.State == States.MatchEnded || Game.State == States.CleanupMatchDetails1 || Game.State == States.CleanupMatchDetails2)
                {
                    CleanupCompleted = true;
                    server.PlayerCleanupCompleted(this);
                }

                // Send player list
                Game.Log("Sending player list to player (" + PlayerId + ").");
                server.SendPlayerList(this);

                // Broadcast to all players
                byte[] playerJoinedPayload = Encoding.ASCII.GetBytes(PlayerId);
                Tuple<short, byte[]> playerJoinedPacket = server.CreatePacket(Packets.PlayerJoined, playerJoinedPayload);
                await server.SendPacket(playerJoinedPacket);

                // Send match state
                byte[] statePayload = BitConverter.GetBytes(Game.State);
                Tuple<short, byte[]> statePacket = CreatePacket(Packets.GameState, statePayload);
                await SendPacket(statePacket);

                // Add to connections list
                server.PlayerJoined(this);

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerJoinedEvent?.Invoke(this, new PlayerJoinedArguments(PlayerId, false, Ready));
                });

                // Request ping every second
                PingTimer = new Stopwatch();
                pingInterval = new Timer(SendPing, null, 0, 1000);
                pingReplyReceived = true;

                Joined = true;
            }
            else if (packet.commandType == 3)
            {
                // <Player>
                // Join reply from host

                byte[] codeBytes = new byte[2];
                System.Buffer.BlockCopy(packet.payload, 0, codeBytes, 0, 2);
                ushort code = BitConverter.ToUInt16(codeBytes, 0);

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    JoinMatchEvent?.Invoke(null, new JoinMatchArguments(
                        code
                    ));
                });

                if (code > 0)
                {
                    this.Close();
                    return;
                }
                else
                {
                    byte[] playerIdBytes = new byte[15];
                    byte[] playerReadyBytes = new byte[2];

                    System.Buffer.BlockCopy(packet.payload, codeBytes.Length, playerIdBytes, 0, playerIdBytes.Length);
                    System.Buffer.BlockCopy(packet.payload, codeBytes.Length + playerIdBytes.Length, playerReadyBytes, 0, playerReadyBytes.Length);

                    PlayerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);
                    Joined = true;

                    ushort playerStatus = BitConverter.ToUInt16(playerReadyBytes, 0);
                    bool playerReady = false;

                    if (playerStatus == 1)
                    {
                        playerReady = true;
                    }

                    // Check ping every second
                    PingTimer = new Stopwatch();
                    PingTimer.Start();
                    pingInterval = new Timer(CheckPing, null, 0, 1000);

                    // Update UI
                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                    {
                        PlayerJoinedEvent?.Invoke(this, new PlayerJoinedArguments(PlayerId, true, playerReady));
                    });
                }
            }
            else if (packet.commandType == 4)
            {
                // <Player>
                // Game state from host

                byte[] stateBytes = new byte[2];
                System.Buffer.BlockCopy(packet.payload, 0, stateBytes, 0, stateBytes.Length);

                ushort state = BitConverter.ToUInt16(stateBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MatchStateEvent?.Invoke(null, new MatchStateArguments(
                        state
                    ));
                });
            }
            else if (packet.commandType == 5)
            {
                // <Host/Player>
                // Leave request from host/player

                leaveRequestReceived = true;

                if (host)
                {
                    // Broadcast to all players
                    byte[] playerLeftPayload = Encoding.ASCII.GetBytes(PlayerId);
                    Tuple<short, byte[]> playerLeftPacket = server.CreatePacket(Packets.PlayerLeft, playerLeftPayload);
                    await server.SendPacket(playerLeftPacket);
                }

                // Stop ping interval
                if (pingInterval != null)
                {
                    pingInterval.Dispose();
                }

                // Remove from connections list
                server.PlayerLeft(this);

                // Update player list and UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerLeftEvent?.Invoke(this, new PlayerLeftArguments(PlayerId, false));
                });

                // Disconnect player
                Tuple<short, byte[]> leaveReplyPacket = CreatePacket(Packets.LeaveReply, null);
                await SendPacket(leaveReplyPacket);

                this.Close();
            }
            else if (packet.commandType == 6)
            {
                // <Host/Player>
                // Leave reply from host/player

                LeaveRequestAccepted = true;

                if (host)
                {
                    Game.Log("Player (" + PlayerId + ") accepted the leave request.");
                }
                else
                {
                    Game.Log("Host accepted the leave request.");
                }

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    LeaveReplyEvent.Invoke(null);
                });
            }
            else if (packet.commandType == 7)
            {
                // <Player>
                // Ping request from host

                Tuple<short, byte[]> pingReplyPacket = CreatePacket(Packets.PingReply, null);
                await SendPacket(pingReplyPacket);

                if (PingTimer != null)
                {
                    PingTimer.Restart();
                }
            }
            else if (packet.commandType == 8 && host)
            {
                // <Host>
                // Ping reply from player

                PingTimer.Stop();
                PingTime = (ulong)PingTimer.ElapsedMilliseconds;

                pingReplyReceived = true;

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerPingEvent?.Invoke(this, new PlayerPingArguments(PlayerId, PingTime));
                });

                // Send to all players
                byte[] playerPingTimePayload = new byte[23];
                byte[] playerIdPaddedBytes = new byte[15];

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(PlayerId);
                byte[] playerPingTimeBytes = BitConverter.GetBytes(PingTime);

                System.Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                System.Buffer.BlockCopy(playerIdPaddedBytes, 0, playerPingTimePayload, 0, playerIdPaddedBytes.Length);
                System.Buffer.BlockCopy(playerPingTimeBytes, 0, playerPingTimePayload, playerIdPaddedBytes.Length, playerPingTimeBytes.Length);

                Tuple<short, byte[]> playerPingTimePacket = server.CreatePacket(Packets.PlayerPing, playerPingTimePayload);
                await server.SendPacket(playerPingTimePacket);
            }
            else if (packet.commandType == 9)
            {
                // <Player>
                // Player from list

                byte[] playerIdBytes = new byte[15];
                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, 15);
                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);

                byte[] playerReadyStatusBytes = new byte[2];
                System.Buffer.BlockCopy(packet.payload, 15, playerReadyStatusBytes, 0, 2);
                ushort playerReadyStatus = BitConverter.ToUInt16(playerReadyStatusBytes, 0);

                bool playerReady = false;

                if (playerReadyStatus == 1)
                {
                    playerReady = true;
                }

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerJoinedEvent?.Invoke(this, new PlayerJoinedArguments(playerId, false, playerReady));
                });
            }
            else if (packet.commandType == 10)
            {
                // <Player>
                // Player joined

                byte[] playerIdBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, packet.payload.Length);
                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerJoinedEvent?.Invoke(this, new PlayerJoinedArguments(playerId, false, false));
                });
            }
            else if (packet.commandType == 11)
            {
                // <Player>
                // Player left

                byte[] playerIdBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, packet.payload.Length);
                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerLeftEvent?.Invoke(this, new PlayerLeftArguments(playerId, false));
                });
            }
            else if (packet.commandType == 12)
            {
                // <Player>
                // Ready time left

                byte[] readyTimeLeftBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, readyTimeLeftBytes, 0, 4);

                uint readyTimeLeft = BitConverter.ToUInt32(readyTimeLeftBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    ReadyTimeEvent?.Invoke(null, new ReadyTimeLeftArguments(readyTimeLeft));
                });
            }
            else if (packet.commandType == 13)
            {
                // <Player>
                // Player ping time

                byte[] playerIdBytes = new byte[15];
                byte[] playerPingTimeBytes = new byte[8];

                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, 15);
                System.Buffer.BlockCopy(packet.payload, 15, playerPingTimeBytes, 0, 8);

                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);
                ulong playerPingTime = BitConverter.ToUInt64(playerPingTimeBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerPingEvent?.Invoke(null, new PlayerPingArguments(playerId, playerPingTime));
                });
            }
            else if (packet.commandType == 14 && host)
            {
                // <Host>
                // Player ready

                byte[] readyStatusBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, readyStatusBytes, 0, packet.payload.Length);
                ushort readyStatus = BitConverter.ToUInt16(readyStatusBytes, 0);

                if (readyStatus == 0)
                {
                    Ready = false;
                }
                else
                {
                    Ready = true;
                }

                // Broadcast to all players
                byte[] playerStatusPayload = new byte[17];
                byte[] playerIdPaddedBytes = new byte[15];
                byte[] playerIdBytes = Encoding.ASCII.GetBytes(PlayerId);
                byte[] playerReadyStatusBytes = BitConverter.GetBytes(readyStatus);

                System.Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                System.Buffer.BlockCopy(playerIdPaddedBytes, 0, playerStatusPayload, 0, playerIdPaddedBytes.Length);
                System.Buffer.BlockCopy(playerReadyStatusBytes, 0, playerStatusPayload, playerIdPaddedBytes.Length, playerReadyStatusBytes.Length);

                Tuple<short, byte[]> playeReadyStatusPacket = server.CreatePacket(Packets.PlayerReady, playerStatusPayload);
                await server.SendPacket(playeReadyStatusPacket);

                // Update ready state
                server.PlayerReady(this);

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerReadyEvent?.Invoke(null, new PlayerReadyArguments(PlayerId, Ready));
                });
            }
            else if (packet.commandType == 15)
            {
                // <Player>
                // Player ready

                byte[] playerIdBytes = new byte[15];
                byte[] playerReadyStatusBytes = new byte[2];

                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, 15);
                System.Buffer.BlockCopy(packet.payload, 15, playerReadyStatusBytes, 0, 2);

                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);
                ushort playerReadyStatus = BitConverter.ToUInt16(playerReadyStatusBytes, 0);

                bool ready = false;

                if (playerReadyStatus == 1)
                {
                    ready = true;
                }

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerReadyEvent?.Invoke(null, new PlayerReadyArguments(playerId, ready));
                });
            }
            else if (packet.commandType == 16)
            {
                // <Player>
                // Load map

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    LoadMapEvent?.Invoke(null);
                });
            }
            else if (packet.commandType == 17)
            {
                // <Player>
                // Map objects created

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MapObjectsCreatedEvent?.Invoke(null);
                });
            }
            else if (packet.commandType == 18 && host)
            {
                // <Host>
                // Map objects requested

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerMapObjectsRequestedEvent?.Invoke(this);
                });
            }
            else if (packet.commandType == 19)
            {
                // <Player>
                // Target position

                byte[] targetXBytes = new byte[4];
                byte[] targetYBytes = new byte[4];

                System.Buffer.BlockCopy(packet.payload, 0, targetXBytes, 0, 4);
                System.Buffer.BlockCopy(packet.payload, targetXBytes.Length, targetYBytes, 0, 4);

                float targetX = BitConverter.ToSingle(targetXBytes, 0);
                float targetY = BitConverter.ToSingle(targetYBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    TargetPositionEvent?.Invoke(null, new TargetPositionArguments(targetX, targetY));
                });
            }
            else if (packet.commandType == 20)
            {
                // <Player>
                // Score

                byte[] playerIdBytes = new byte[15];
                byte[] scoreBytes = new byte[2];

                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, 15);
                System.Buffer.BlockCopy(packet.payload, playerIdBytes.Length, scoreBytes, 0, scoreBytes.Length);

                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);
                ushort score = BitConverter.ToUInt16(scoreBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerScoreEvent?.Invoke(null, new PlayerScoreArguments(playerId, score));
                });
            }
            else if (packet.commandType == 21)
            {
                // <Player>
                // Map data end

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MapDataEndEvent?.Invoke(null);
                });
            }
            else if (packet.commandType == 22 && host)
            {
                // <Host>
                // Player map loaded

                if (!MapLoaded)
                {
                    MapLoaded = true;
                    server.PlayerMapLoaded(this);
                }
            }
            else if (packet.commandType == 23)
            {
                // <Player>
                // Show map

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    ShowMapEvent?.Invoke(this);
                });
            }
            else if (packet.commandType == 24 && host)
            {
                // <Host>
                // Player map shown

                if (!MapShown)
                {
                    MapShown = true;
                    server.PlayerMapShown(this);
                }
            }
            else if (packet.commandType == 25)
            {
                // <Player>
                // Match countdown

                byte[] countdownBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, countdownBytes, 0, 4);

                uint countdownSeconds = BitConverter.ToUInt32(countdownBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MatchCountdownEvent?.Invoke(this, new MatchCountdownArguments(countdownSeconds));
                });
            }
            else if (packet.commandType == 26)
            {
                // <Player>
                // Match started

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MatchStartedEvent?.Invoke(this);
                });
            }
            else if (packet.commandType == 27 && host)
            {
                // <Host>
                // Player position

                byte[] playerXBytes1 = new byte[4];
                byte[] playerYBytes1 = new byte[4];
                byte[] playerAngleBytes1 = new byte[4];

                System.Buffer.BlockCopy(packet.payload, 0, playerXBytes1, 0, 4);
                System.Buffer.BlockCopy(packet.payload, playerXBytes1.Length, playerYBytes1, 0, 4);
                System.Buffer.BlockCopy(packet.payload, playerXBytes1.Length + playerYBytes1.Length, playerAngleBytes1, 0, 4);

                PlayerX = BitConverter.ToSingle(playerXBytes1, 0);
                PlayerY = BitConverter.ToSingle(playerYBytes1, 0);
                PlayerAngle = BitConverter.ToSingle(playerAngleBytes1, 0);

                // Broadcast to all players
                byte[] positionPayload = new byte[27];
                byte[] playerIdPaddedBytes = new byte[15];

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(PlayerId);
                byte[] playerX = BitConverter.GetBytes(PlayerX);
                byte[] playerY = BitConverter.GetBytes(PlayerY);
                byte[] playerAngle = BitConverter.GetBytes(PlayerAngle);

                System.Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                System.Buffer.BlockCopy(playerIdPaddedBytes, 0, positionPayload, 0, playerIdPaddedBytes.Length);
                System.Buffer.BlockCopy(playerX, 0, positionPayload, playerIdPaddedBytes.Length, playerX.Length);
                System.Buffer.BlockCopy(playerY, 0, positionPayload, playerIdPaddedBytes.Length + playerX.Length, playerY.Length);
                System.Buffer.BlockCopy(playerAngle, 0, positionPayload, playerIdPaddedBytes.Length + playerX.Length + playerY.Length, playerAngle.Length);

                Tuple<short, byte[]> playerPositionPacket = server.CreatePacket(Packets.PlayerPosition, positionPayload);
                await server.SendPacket(playerPositionPacket);

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerPositionEvent?.Invoke(null, new PlayerPositionArguments(PlayerId, PlayerX, PlayerY, PlayerAngle));
                });
            }
            else if (packet.commandType == 28)
            {
                // <Player>
                // Player position

                byte[] playerIdBytes = new byte[15];
                byte[] playerXBytes = new byte[4];
                byte[] playerYBytes = new byte[4];
                byte[] playerAngleBytes = new byte[4];

                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, 15);
                System.Buffer.BlockCopy(packet.payload, playerIdBytes.Length, playerXBytes, 0, playerXBytes.Length);
                System.Buffer.BlockCopy(packet.payload, playerIdBytes.Length + playerXBytes.Length, playerYBytes, 0, playerYBytes.Length);
                System.Buffer.BlockCopy(packet.payload, playerIdBytes.Length + playerXBytes.Length + playerYBytes.Length, playerAngleBytes, 0, playerAngleBytes.Length);

                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);
                float playerX = BitConverter.ToSingle(playerXBytes, 0);
                float playerY = BitConverter.ToSingle(playerYBytes, 0);
                float playerAngle = BitConverter.ToSingle(playerAngleBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerPositionEvent?.Invoke(null, new PlayerPositionArguments(playerId, playerX, playerY, playerAngle));
                });
            }
            else if (packet.commandType == 29)
            {
                // <Player>
                // Match time left

                byte[] timeLeftBytes = new byte[packet.payload.Length];
                System.Buffer.BlockCopy(packet.payload, 0, timeLeftBytes, 0, 4);

                uint timeLeftSeconds = BitConverter.ToUInt32(timeLeftBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MatchTimeLeftEvent?.Invoke(null, new MatchTimeLeftArguments(timeLeftSeconds));
                });
            }
            else if (packet.commandType == 30 && host)
            {
                // <Host>
                // Target reached

                TargetIndex++;

                // Sent to all players
                byte[] targetReachedPayload = new byte[17];
                byte[] playerIdPaddedBytes = new byte[15];

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(PlayerId);
                byte[] targetIndex = BitConverter.GetBytes(TargetIndex);

                System.Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                System.Buffer.BlockCopy(playerIdPaddedBytes, 0, targetReachedPayload, 0, playerIdPaddedBytes.Length);
                System.Buffer.BlockCopy(targetIndex, 0, targetReachedPayload, playerIdPaddedBytes.Length, targetIndex.Length);

                Tuple<short, byte[]> targetReachedPacket = server.CreatePacket(Packets.PlayerTargetReached, targetReachedPayload);
                await server.SendPacket(targetReachedPacket);

                // Update UI
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerTargetReachedEvent?.Invoke(null, new PlayerTargetReachedArguments(PlayerId, TargetIndex));
                });
            }
            else if (packet.commandType == 31)
            {
                // <Player>
                // Target reached

                byte[] playerIdBytes = new byte[15];
                byte[] playerTargetIndexBytes = new byte[2];

                System.Buffer.BlockCopy(packet.payload, 0, playerIdBytes, 0, playerIdBytes.Length);
                System.Buffer.BlockCopy(packet.payload, playerIdBytes.Length, playerTargetIndexBytes, 0, playerTargetIndexBytes.Length);

                string playerId = Encoding.UTF8.GetString(playerIdBytes).Replace("\0", String.Empty);
                ushort playerTargetIndex = BitConverter.ToUInt16(playerTargetIndexBytes, 0);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    PlayerTargetReachedEvent?.Invoke(null, new PlayerTargetReachedArguments(playerId, playerTargetIndex));
                });
            }
            else if (packet.commandType == 32)
            {
                // <Player>
                // Match ended

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    MatchEndedEvent?.Invoke(this);
                });
            }
            else if (packet.commandType == 33)
            {
                // <Player>
                // Load match details

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    LoadMatchDetailsEvent?.Invoke(this);
                });
            }
            else if (packet.commandType == 34 && host)
            {
                // <Host>
                // Cleanup completed

                if (!CleanupCompleted)
                {
                    CleanupCompleted = true;
                    server.PlayerCleanupCompleted(this);
                }
            }
            else if (packet.commandType == 35)
            {
                // <Player>
                // Show match details

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    ShowMatchDetailsEvent?.Invoke(this);
                });
            }
        }

        private void CheckPing(object state)
        {
            if (PingTimer.ElapsedMilliseconds > MaxPingTime)
            {
                Game.Log("Player (" + PlayerId + ") connection lost.");
                this.Close();
            }
        }

        public async void SendPingTest()
        {
            PingTimer = new Stopwatch();

            Game.Log("Sending ping request to " + StreamSocket.Information.RemoteAddress + " (" + StreamSocket.Information.RemotePort + ")...");
            Tuple<short, byte[]> pingPacket = CreatePacket(Packets.PingTestRequest, null);
            await SendPacket(pingPacket);

            PingTimer.Restart();
        }

        private async void SendPing(object state)
        {
            CheckPing(null);

            if (pingReplyReceived)
            {
                // Game.Log("Sending ping request to player (" + PlayerId + ").");

                pingReplyReceived = false;
                PingTimer.Restart();

                Tuple<short, byte[]> pingPacket = CreatePacket(Packets.PingRequest, null);
                await SendPacket(pingPacket);
            }
            else
            {
                Game.Log("Ping from player (" + PlayerId + ") not yet received.");
            }
        }

        public Tuple<short, byte[]> CreatePacket(short commandShort, byte[] payloadBytes)
        {
            byte[] commandBytes = BitConverter.GetBytes(commandShort);

            short payloadSize = 0;

            if (payloadBytes != null)
            {
                payloadSize = (short)payloadBytes.Length;
            }

            byte[] payloadSizeBytes = BitConverter.GetBytes(payloadSize);

            if (payloadBytes == null)
            {
                payloadBytes = new byte[0];
            }

            byte[] packet = new byte[commandBytes.Length + payloadSizeBytes.Length + payloadBytes.Length];

            System.Buffer.BlockCopy(commandBytes, 0, packet, 0, commandBytes.Length);
            System.Buffer.BlockCopy(payloadSizeBytes, 0, packet, commandBytes.Length, payloadSizeBytes.Length);
            System.Buffer.BlockCopy(payloadBytes, 0, packet, commandBytes.Length + payloadSizeBytes.Length, payloadBytes.Length);

            return new Tuple<short, byte[]>(commandShort, packet);
        }

        public async Task SendPacket(Tuple<short, byte[]> packet)
        {
            if (!connected)
            {
                return;
            }

            try
            {
                dataWriter.WriteBytes(packet.Item2);
                await dataWriter.StoreAsync();

                if (packet.Item1 != Packets.PingRequest && packet.Item1 != Packets.PingReply && packet.Item1 != Packets.PlayerPing &&
                    packet.Item1 != Packets.Position && packet.Item1 != Packets.PlayerPosition)
                {
                    Game.Log("Packet Sent (" + packet.Item1 + ") " + ByteArray2String(packet.Item2) + ".");
                }
            }
            catch { }
        }

        // Debug
        private static string ByteArray2String(byte[] bytes)
        {
            var stringBuilder = new StringBuilder("[ ");

            foreach (var b in bytes)
            {
                stringBuilder.Append(b + ", ");
            }

            stringBuilder.Append("]");

            return stringBuilder.ToString();
        }
    }
}