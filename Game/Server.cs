using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Networking.Sockets;
using Windows.UI.Core;
using static Game.Events;
using static Game.MainPage;

namespace Game
{
    public class Server
    {
        public bool Listening { get; set; }
        public List<Connection> Connections { get; set; }

        private const int MapLoadTimeLimit = 10 * 1000;
        private const int MapTransitionTimeLimit = 10 * 1000;
        private const int CleanupTimeLimit = 15 * 1000;

        private StreamSocketListener streamSocketListener = null;

        public bool Ready { get; set; }
        public bool MapLoaded { get; set; }
        public bool MapShown { get; set; }
        public bool CleanupCompleted { get; set; }

        public bool PlayersReadyLocked { get; set; }
        public bool PlayersMapLoadedLocked { get; set; }
        public bool PlayersMapShownLocked { get; set; }
        public bool PlayersCleanupCompletedLocked { get; set; }

        private SemaphoreSlim playersReadySemaphore = null;
        private SemaphoreSlim playersMapLoadedSemaphore = null;
        private SemaphoreSlim playersMapShownSemaphore = null;
        private SemaphoreSlim playerCleanupCompletedSemaphore = null;

        private Timer mapLoadedTimer = null;
        private Timer mapShownTimer = null;
        private Timer cleanupCompletedTimer = null;

        public event NewConnectionHandler NewConnectionEvent;
        public event LoadMapHandler LoadMapEvent;
        public event ShowMapHandler ShowMapEvent;
        public event ShowCountdownHandler ShowCountdownEvent;
        public event ShowMatchDetailsHandler ShowMatchDetailsEvent;

        public Server()
        {
            Connections = new List<Connection>();
            playersReadySemaphore = new SemaphoreSlim(1, 1);
            playersMapLoadedSemaphore = new SemaphoreSlim(1, 1);
            playersMapShownSemaphore = new SemaphoreSlim(1, 1);
            playerCleanupCompletedSemaphore = new SemaphoreSlim(1, 1);
        }

        public async Task Start(int port)
        {
            streamSocketListener = new StreamSocketListener();
            streamSocketListener.Control.QualityOfService = SocketQualityOfService.LowLatency;
            streamSocketListener.ConnectionReceived += OnConnection;

            await streamSocketListener.BindServiceNameAsync(port.ToString());
            Listening = true;

            Game.Log("Server started (" + port + ").");
        }

        public void Stop()
        {
            for (int i = 0; i < Connections.Count; i++)
            {
                Connection connection = Connections[i];
                connection.Close();
            }

            streamSocketListener.Dispose();

            Listening = false;
            Game.Log("Server stopped.");
        }

        private void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            StreamSocket streamSocket = args.Socket;

            Connection connection = new Connection(this, streamSocket);

            NewConnectionEvent?.Invoke(null, new NewConnectionArguments(
                connection
            ));
        }

        public void PlayerJoined(Connection connection)
        {
            Connections.Add(connection);
        }

        public async void SendPlayerList(Connection connection)
        {
            for (int i = 0; i < Connections.Count; i++)
            {
                Connection playerConnection = Connections[i];

                if (playerConnection.PlayerId != null &&
                    playerConnection.PlayerId == connection.PlayerId)
                {
                    continue;
                }

                byte[] playerListPayload = new byte[17];
                byte[] playerIdPaddedBytes = new byte[15];

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(playerConnection.PlayerId);
                Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                ushort readyStatus = 0;

                if (playerConnection.Ready)
                {
                    readyStatus = 1;
                }

                byte[] readyStatusBytes = BitConverter.GetBytes(readyStatus);

                Buffer.BlockCopy(playerIdPaddedBytes, 0, playerListPayload, 0, playerIdPaddedBytes.Length);
                Buffer.BlockCopy(readyStatusBytes, 0, playerListPayload, playerIdPaddedBytes.Length, readyStatusBytes.Length);

                Tuple<short, byte[]> playerListPacket = connection.CreatePacket(Packets.PlayerList, playerListPayload);
                await connection.SendPacket(playerListPacket);
            }
        }

        public void PlayerLeft(Connection connection)
        {
            for (int i = 0; i < Connections.Count; i++)
            {
                if (Connections[i].PlayerId == connection.PlayerId)
                {
                    Connections.Remove(connection);
                }
            }
        }

        public void HostReady(bool ready)
        {
            this.Ready = ready;

            if (ready)
            {
                Game.Log("Ready status changed (Ready).");
            }
            else
            {
                Game.Log("Ready status changed (Not ready).");
            }

            if (Connections.Count > -1)
            {
                PlayersReady();
            }
        }

        public void PlayerReady(Connection connection)
        {
            if (connection.Ready)
            {
                Game.Log("Player (" + connection.PlayerId + ") ready status changed (Ready).");
            }
            else
            {
                Game.Log("Player (" + connection.PlayerId + ") ready status changed (Not ready).");
            }

            if (Game.State == States.MatchDetails || Game.State >= States.MatchEnded)
            {
                // Check if host and
                // all players ready
                PlayersReady();
            }
        }

        private bool PlayersReady()
        {
            bool playersReady = Ready;

            for (int i = 0; i < Connections.Count; i++)
            {
                if (!Connections[i].Ready)
                {
                    playersReady = false;
                }
            }

            if (playersReady)
            {
                ShowLoading(null, null);
            }

            return playersReady;
        }

        private async void ShowLoading(object sender, object e)
        {
            await playersReadySemaphore.WaitAsync();

            if (PlayersReadyLocked)
            {
                playersReadySemaphore.Release();
                return;
            }

            PlayersReadyLocked = true;
            Game.Log("All players are ready.");

            // Reset
            Game.Ready = false;
            this.Ready = false;

            for (int i = 0; i < Connections.Count; i++)
            {
                if (Connections[i].Ready)
                {
                    Connections[i].Ready = false;
                }
            }

            // Update UI
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                LoadMapEvent?.Invoke(null);
            });

            playersReadySemaphore.Release();
        }

        public void HostMapLoaded()
        {
            MapLoaded = true;
            Game.Log("Map loaded.");

            // Check if host and players' map
            // have loaded
            if (!PlayersMapLoaded())
            {
                // Show map anyways
                // after 10 seconds
                mapLoadedTimer = new Timer(ShowMap, true, MapLoadTimeLimit, Timeout.Infinite);
            }
        }

        public void PlayerMapLoaded(object source)
        {
            Connection connection = (Connection)source;
            Game.Log("Player (" + connection.PlayerId + ") map loaded.");

            if (Game.State == States.Loading2)
            {
                // Check if host and players' map
                // have loaded
                PlayersMapLoaded();
            }
        }

        private bool PlayersMapLoaded()
        {
            bool playersMapLoaded = MapLoaded;

            for (int i = 0; i < Connections.Count; i++)
            {
                if (!Connections[i].MapLoaded)
                {
                    playersMapLoaded = false;
                }
            }

            if (playersMapLoaded)
            {
                ShowMap(false);
            }

            return playersMapLoaded;
        }

        private async void ShowMap(object timeout)
        {
            await playersMapLoadedSemaphore.WaitAsync();

            if (PlayersMapLoadedLocked)
            {
                playersMapLoadedSemaphore.Release();
                return;
            }

            PlayersMapLoadedLocked = true;

            if ((bool)timeout)
            {
                Game.Log("Map from all players have loaded.");
            }
            else
            {
                Game.Log("Map from all players not yet loaded, showing map...");
            }

            // Stop timer
            if (mapLoadedTimer != null)
            {
                mapLoadedTimer.Dispose();
            }

            // Update UI
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                ShowMapEvent?.Invoke(null);
            });

            playersMapLoadedSemaphore.Release();
        }

        public void HostMapShown()
        {
            MapShown = true;
            Game.Log("Map is on the screen.");

            if (!PlayersMapShown())
            {
                // Start match countdown
                // anyways after 10 seconds
                mapShownTimer = new Timer(ShowCountdown, true, MapTransitionTimeLimit, Timeout.Infinite);
            }
        }

        public void PlayerMapShown(object source)
        {
            Connection connection = (Connection)source;
            Game.Log("Player (" + connection.PlayerId + ") map shown.");

            if (Game.State == States.Map && Game.Transition == false)
            {
                // Check if host and players' map
                // have been shown
                PlayersMapShown();
            }
        }

        private bool PlayersMapShown()
        {
            bool playersMapShown = MapShown;

            for (int i = 0; i < Connections.Count; i++)
            {
                if (!Connections[i].MapShown)
                {
                    playersMapShown = false;
                }
            }

            if (playersMapShown)
            {
                ShowCountdown(false);
            }

            return playersMapShown;
        }

        private async void ShowCountdown(object timeout)
        {
            await playersMapShownSemaphore.WaitAsync();

            if (PlayersMapShownLocked)
            {
                playersMapShownSemaphore.Release();
                return;
            }

            PlayersMapShownLocked = true;

            if ((bool)timeout)
            {
                Game.Log("Map from all players have been shown.");
            }
            else
            {
                Game.Log("Map from all players not yet shown, starting countdown...");
            }

            // Stop timer
            if (mapShownTimer != null)
            {
                mapShownTimer.Dispose();
            }

            // Update UI
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                ShowCountdownEvent?.Invoke(null);
            });

            playersMapShownSemaphore.Release();
        }

        public void HostCleanupCompleted()
        {
            CleanupCompleted = true;
            Game.Log("Cleanup completed.");

            // Check if host and players'
            // cleanup is completed
            if (!PlayersCleanupCompleted())
            {
                // Show match details anyways
                // after 10 seconds
                cleanupCompletedTimer = new Timer(ShowMatchDetails, true, CleanupTimeLimit, Timeout.Infinite);
            }
        }

        public void PlayerCleanupCompleted(object source)
        {
            Connection connection = (Connection)source;
            Game.Log("Player (" + connection.PlayerId + ") cleanup completed.");

            if (Game.State == States.CleanupMatchDetails2)
            {
                // Check if host and players' cleanup
                // are completed
                PlayersCleanupCompleted();
            }
        }

        private bool PlayersCleanupCompleted()
        {
            if (PlayersCleanupCompletedLocked)
            {
                return true;
            }

            bool playersCleanupCompleted = CleanupCompleted;

            for (int i = 0; i < Connections.Count; i++)
            {
                if (!Connections[i].CleanupCompleted)
                {
                    playersCleanupCompleted = false;
                }
            }

            if (playersCleanupCompleted)
            {
                ShowMatchDetails(false);
            }

            return playersCleanupCompleted;
        }

        private async void ShowMatchDetails(object timeout)
        {
            await playerCleanupCompletedSemaphore.WaitAsync();

            if (PlayersCleanupCompletedLocked)
            {
                playerCleanupCompletedSemaphore.Release();
                return;
            }

            PlayersCleanupCompletedLocked = true;


            if ((bool)timeout)
            {
                Game.Log("Cleanup of all players is completed.");
            }
            else
            {
                Game.Log("Cleanup of all players not yet completed, moving to match details...");
            }

            // Stop timer
            if (cleanupCompletedTimer != null)
            {
                cleanupCompletedTimer.Dispose();
            }

            CleanupCompleted = false;


            for (int i = 0; i < Connections.Count; i++)
            {
                Connection connection = (Connection)Connections[i];

                if (!connection.Joined)
                {
                    continue;
                }

                if (connection.CleanupCompleted)
                {
                    // Reset cleanup state
                    Connections[i].CleanupCompleted = false;
                }
                else
                {
                    // Kick out-of-sync
                    // players
                    if (connection.PlayerId != null)
                    {
                        Game.Log("Player (" + connection.PlayerId + ") out of sync.");
                        connection.Close();
                    }
                }
            }

            // Update UI
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                ShowMatchDetailsEvent?.Invoke(null);
            });

            playerCleanupCompletedSemaphore.Release();
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

            Buffer.BlockCopy(commandBytes, 0, packet, 0, commandBytes.Length);
            Buffer.BlockCopy(payloadSizeBytes, 0, packet, commandBytes.Length, payloadSizeBytes.Length);
            Buffer.BlockCopy(payloadBytes, 0, packet, commandBytes.Length + payloadSizeBytes.Length, payloadBytes.Length);

            return new Tuple<short, byte[]>(commandShort, packet);
        }

        public async Task SendPacket(Tuple<short, byte[]> packet)
        {
            for (int i = 0; i < Connections.Count; i++)
            {
                if (!Connections[i].Joined)
                {
                    continue;
                }

                await Connections[i].SendPacket(packet);
            }
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
