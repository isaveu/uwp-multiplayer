using System;
using Windows.ApplicationModel.Core;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.Networking.Connectivity;
using Windows.Web.Http;
using Windows.ApplicationModel;
using Windows.Web.Http.Filters;
using Newtonsoft.Json;
using Open.Nat;
using static Game.Events;

namespace Game
{
    public sealed partial class MainPage : Page
    {
        private const string ServerHost = "https://matchmaking-uwp.appspot.com";
        private const string PathGeneral = "/general.json";
        private const string PathMatches = "/matches.json";
        private const string PathCreateMatch = "/create-match";
        private const string PathPing = "/ping";

        private const int ErrorDisplayTime = 500;
        private const uint MaxPingTime = 800;

        private const uint ReadyTime = 60;
        private const uint CountdownTime = 3;
        private const uint MatchTime = 30;

        private const float MaxMapWidth = 1280;
        private const float MaxMapHeight = 720;

        private int port = 0;
        private ContentDialog contentDialogPort = null;

        private ContentDialog contentDialogPlayerId = null;
        private List<JsonMatch> matches = null;

        private List<JsonMatch> qualityMatches = null;
        private int qualityMatchesIndex = 0;

        private bool serverConnected = false;
        private SemaphoreSlim serverConnectionClosedSemaphore = null;
        private bool serverConnectionClosedLocked = false;
        private Timer serverPingInterval = null;

        private Server server = null;
        private Connection connection = null;
        private SemaphoreSlim connectionClosedSemaphore = null;
        private bool connectionClosedLocked = false;
        private bool host = false;

        private List<Player> players = null;
        private Player localPlayer = null;

        private DispatcherTimer readyTimeLeftInterval = null;
        private uint readyTimeLeftSecondsPassed = 0;

        private SemaphoreSlim playersCarSemaphore = null;

        private DispatcherTimer countdownInterval = null;
        private uint countdownSecondsPassed = 0;

        private Storyboard storyboardCountdown;
        private bool matchTimeEnded = false;

        private DispatcherTimer timeLeftInterval = null;
        private uint timeLeftSecondsPassed = 0;

        private Timer leaveMatchTimer = null;
        private SemaphoreSlim leaveMatchSemaphore = null;
        private bool leaveMatchLocked = false;
        private bool leavingMatch = false;

        private bool carControllable = false;
        private bool left = false;
        private bool up = false;
        private bool right = false;
        private bool down = false;

        private List<Target> targets = null;
        private uint targetsCount = 5;

        private ushort targetIndex = 0;
        private ushort remoteTargetIndex = 0;
        private TextBlock textBlockCountdown = null;

        private SemaphoreSlim matchEndedSemaphore = null;
        private bool matchEndedLocked = false;

        // Events
        public delegate void NewConnectionHandler(object source, NewConnectionArguments e);
        public delegate void PingTestReplyHandler(object source, PingTestReplyArguments e);

        public delegate void JoinMatchHandler(object source, JoinMatchArguments e);
        public delegate void MatchStateHandler(object source, MatchStateArguments e);
        public delegate void PlayerJoinedHandler(object source, PlayerJoinedArguments e);
        public delegate void PlayerLeftHandler(object source, PlayerLeftArguments e);
        public delegate void ReadyTimeHandler(object source, ReadyTimeLeftArguments e);
        public delegate void PlayerPingHandler(object source, PlayerPingArguments e);
        public delegate void PlayerReadyHandler(object source, PlayerReadyArguments e);
        public delegate void LoadMapHandler(object source);

        public delegate void MapObjectsCreatedHandler(object source);
        public delegate void PlayerMapObjectsRequestedHandler(object source);
        public delegate void PlayerPositionHandler(object source, PlayerPositionArguments e);
        public delegate void TargetPositionHandler(object source, TargetPositionArguments e);
        public delegate void PlayerScoreHandler(object source, PlayerScoreArguments e);
        public delegate void MapDataEndHandler(object source);
        public delegate void ShowMapHandler(object source);
        public delegate void ShowCountdownHandler(object source);
        public delegate void MatchCountdownHandler(object source, MatchCountdownArguments e);
        public delegate void MatchStartedHandler(object source);
        public delegate void MatchTimeLeftHandler(object source, MatchTimeLeftArguments e);
        public delegate void PlayerTargetReachedHandler(object source, PlayerTargetReachedArguments e);
        public delegate void MatchEndedHandler(object source);
        public delegate void LeaveReplyHandler(object source);
        public delegate void LoadMatchDetailsHandler(object source);
        public delegate void ShowMatchDetailsHandler(object source);

        public MainPage()
        {
            Game.Log("Version: " + GetAppVersion());

            ApplicationView applicationView = ApplicationView.GetForCurrentView();
            applicationView.TitleBar.BackgroundColor = Color.FromArgb(255, 36, 36, 36);
            applicationView.TitleBar.ForegroundColor = Colors.White;
            applicationView.TitleBar.InactiveBackgroundColor = Color.FromArgb(225, 50, 50, 50);
            applicationView.TitleBar.ButtonBackgroundColor = applicationView.TitleBar.BackgroundColor;
            applicationView.TitleBar.ButtonForegroundColor = Colors.White;
            applicationView.TitleBar.ButtonInactiveBackgroundColor = applicationView.TitleBar.BackgroundColor;
            applicationView.TitleBar.ButtonInactiveForegroundColor = Colors.White;

            this.InitializeComponent();

            serverConnectionClosedSemaphore = new SemaphoreSlim(1, 1);
            connectionClosedSemaphore = new SemaphoreSlim(1, 1);
            playersCarSemaphore = new SemaphoreSlim(1, 1);
            matchEndedSemaphore = new SemaphoreSlim(1, 1);
            leaveMatchSemaphore = new SemaphoreSlim(1, 1);

            Window.Current.CoreWindow.KeyDown += PageKeyDownHandler;
            Window.Current.CoreWindow.KeyUp += PageKeyUpHandler;
            SystemNavigationManager.GetForCurrentView().BackRequested += SendLeaveRequest;
        }

        public static string GetAppVersion()
        {
            Package package = Package.Current;
            PackageId packageId = package.Id;
            PackageVersion version = packageId.Version;

            return string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
        }

        private void PageKeyDownHandler(CoreWindow sender, KeyEventArgs e)
        {
            if (Game.State == States.Paused)
            {
                TextBlockPaused.Visibility = Visibility.Collapsed;

                Game.State = States.Started;
                ShowPlayerIdDialog();
            }

            if (e.VirtualKey == VirtualKey.R)
            {
                if (Game.State == States.MatchDetails && Game.Transition == false)
                {
                    ToggleReadyStatus();
                }
            }

            if (carControllable)
            {
                if (e.VirtualKey == VirtualKey.Left)
                {
                    left = true;
                }

                if (e.VirtualKey == VirtualKey.Up)
                {
                    up = true;
                }

                if (e.VirtualKey == VirtualKey.Right)
                {
                    right = true;
                }

                if (e.VirtualKey == VirtualKey.Down)
                {
                    down = true;
                }
            }
        }

        private void PageKeyUpHandler(CoreWindow sender, KeyEventArgs e)
        {
            if (e.VirtualKey == VirtualKey.Left)
            {
                left = false;
            }

            if (e.VirtualKey == VirtualKey.Up)
            {
                up = false;
            }

            if (e.VirtualKey == VirtualKey.Right)
            {
                right = false;
            }

            if (e.VirtualKey == VirtualKey.Down)
            {
                down = false;
            }
        }

        private async void ShowPlayerIdDialog()
        {
            StackPanel stackPanel = new StackPanel();

            TextBox textBox = new TextBox()
            {
                PlaceholderText = "Player1234",
                MaxLength = 15
            };

            textBox.KeyDown += new KeyEventHandler(PlayerIdOnKeyDownHandler);
            stackPanel.Children.Add(textBox);

            contentDialogPlayerId = new ContentDialog()
            {
                Title = "Player ID",
                MaxWidth = this.ActualWidth,
                Content = stackPanel,
                RequestedTheme = ElementTheme.Dark,
                Background = new SolidColorBrush(Color.FromArgb(255, 19, 19, 19)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 88, 86, 214))
            };

            contentDialogPlayerId.PrimaryButtonText = "Continue";
            contentDialogPlayerId.Closed += delegate
            {
                Game.Log("Player ID dialog closed.");
                CheckPlayerId(textBox.Text);
            };

            Game.Log("Player ID dialog opened.");
            await contentDialogPlayerId.ShowAsync();
        }

        private void PlayerIdOnKeyDownHandler(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && contentDialogPlayerId != null)
            {
                contentDialogPlayerId.Hide();
            }
        }

        private void CheckPlayerId(string userPlayerId)
        {
            userPlayerId = userPlayerId.Trim();

            if (userPlayerId.Length > 0)
            {
                Game.PlayerId = userPlayerId;
                Game.Log("Player ID set (" + Game.PlayerId + ").");
                ShowPortDialog();
            }
            else
            {
                ShowPlayerIdDialog();
            }
        }

        private async void ShowPortDialog()
        {
            StackPanel stackPanel = new StackPanel();

            TextBox textBox = new TextBox()
            {
                Text = "49151",
                MaxLength = 5
            };

            textBox.SelectionStart = textBox.Text.Length;
            textBox.KeyDown += new KeyEventHandler(PortOnKeyDownHandler);
            stackPanel.Children.Add(textBox);

            contentDialogPort = new ContentDialog()
            {
                Title = "Port",
                MaxWidth = this.ActualWidth,
                Content = stackPanel,
                RequestedTheme = ElementTheme.Dark,
                Background = new SolidColorBrush(Color.FromArgb(255, 19, 19, 19)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 88, 86, 214))
            };

            contentDialogPort.PrimaryButtonText = "Continue";
            contentDialogPort.Closed += delegate
            {
                Game.Log("Port dialog closed.");
                CheckPort(textBox.Text);
            };

            Game.Log("Port dialog opened.");
            await contentDialogPort.ShowAsync();
        }

        private void PortOnKeyDownHandler(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number9 ||
                e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad9)
            {
                // Numbers
            }
            else if (e.Key == VirtualKey.Enter && contentDialogPort != null)
            {
                e.Handled = true;
                contentDialogPort.Hide();
            }
            else
            {
                e.Handled = true;
            }
        }

        private async void CheckPort(string userPort)
        {
            if (userPort.Length > 0)
            {
                bool numeric = Int32.TryParse(userPort, out port);

                if (!numeric)
                {
                    Game.Log("Error parsing port.");

                    ShowPortDialog();
                    return;
                }

                if (port < 49151 || port > 65535)
                {
                    Game.Log("Port range is invalid. (" + port + ")");

                    ShowPortDialog();
                    return;
                }

                try
                {
                    server = new Server();

                    // Try to bind port
                    await server.Start(port);
                    server.Stop();

                    // Map port
                    var discoverer = new NatDiscoverer();
                    var cts = new CancellationTokenSource(10000);
                    var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

                    await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, "Game"));
                    Game.Log("Port mapped.");

                    ConnectServer();
                }
                catch (Exception e)
                {
                    Game.Log("Error binding port. (" + port + ")");
                    ShowPortDialog();
                }
            }
            else
            {
                Game.Log("Port textbox is empty.");
                ShowPortDialog();
            }
        }

        private async void ConnectServer()
        {
            ContentPresenter contentPresenter = new ContentPresenter()
            {
                ContentTemplate = Application.Current.Resources["UserMessage"] as DataTemplate
            };

            GridMain.Children.Add(contentPresenter);

            JsonGeneral jsonGeneral = await DownloadGeneralFile();

            if (contentPresenter != null)
            {
                GridMain.Children.Remove(contentPresenter);
            }

            if (jsonGeneral == null)
            {
                MessageDialog messageDialog = new MessageDialog("Connection to server failed.");
                messageDialog.Commands.Add(new UICommand("Retry", new UICommandInvokedHandler(this.ServerCommandHandler)));
                messageDialog.DefaultCommandIndex = 0;

                Game.Log("Server connection failed dialog opened.");
                await messageDialog.ShowAsync();
                Game.Log("Server connection failed dialog closed.");

                return;
            }

            if (jsonGeneral.version != null && jsonGeneral.update_url != null)
            {
                Version appVersion = new Version(GetAppVersion());
                Version latestVersion = new Version(jsonGeneral.version);

                if (latestVersion.CompareTo(appVersion) > 0)
                {
                    Game.Log("New update found. (" + jsonGeneral.version + ")");
                    ShowUpdateDialog(jsonGeneral.version, jsonGeneral.update_url);
                    return;
                }
            }

            if (jsonGeneral.message != null)
            {
                MessageDialog messageDialog = new MessageDialog(jsonGeneral.message);
                messageDialog.Commands.Add(new UICommand("Close", null));
                messageDialog.DefaultCommandIndex = 0;
                await messageDialog.ShowAsync();
            }

            if (jsonGeneral.maintenance)
            {
                Game.Log("Matchmaking server is on maintenance.");

                if (jsonGeneral.message == null)
                {
                    MessageDialog messageDialog = new MessageDialog("Server is on maintenance.");
                    messageDialog.Commands.Add(new UICommand("Retry", new UICommandInvokedHandler(this.ServerCommandHandler)));
                    messageDialog.DefaultCommandIndex = 0;

                    Game.Log("Server on maintenance dialog opened.");
                    await messageDialog.ShowAsync();
                    Game.Log("Server on maintenance dialog closed.");
                }
            }
            else
            {
                Game.Log("Connected to matchmaking server.");

                Game.Transition = true;
                Game.State = States.Matchmaking;

                players = new List<Player>();
                TextBlockLine1.Text = "Searching for matches...";

                Storyboard storyboard = (Storyboard)Resources["StoryboardMatchmakingOpacity1"];
                storyboard.Completed += MatchmakingFadedIn1;
                storyboard.Begin();
            }
        }

        private async Task<JsonGeneral> DownloadGeneralFile()
        {
            JsonGeneral jsonGeneral = null;

            Uri requestUri = new Uri(ServerHost + PathGeneral + "?player_id=" + Game.PlayerId);

            HttpBaseProtocolFilter httpProtocolFilter = new HttpBaseProtocolFilter();
            httpProtocolFilter.CacheControl.ReadBehavior = HttpCacheReadBehavior.NoCache;

            HttpClient httpClient = new HttpClient(httpProtocolFilter);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Game");
            httpClient.DefaultRequestHeaders.Add("X-Version", GetAppVersion());
            HttpResponseMessage httpResponse = new HttpResponseMessage();
            string httpResponseBody = String.Empty;

            try
            {
                httpResponse = await httpClient.GetAsync(requestUri);

                if (httpResponse.IsSuccessStatusCode)
                {
                    httpResponseBody = await httpResponse.Content.ReadAsStringAsync();
                    jsonGeneral = JsonConvert.DeserializeObject<JsonGeneral>(httpResponseBody);
                }
                else
                {
                    Game.Log("Error downloading general.json from server. (Code: " + httpResponse.StatusCode + ")");

                    if (httpResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        jsonGeneral.maintenance = true;
                    }
                }
            }
            catch (Exception e)
            {
                Game.Log("Error downloading general.json from server.");
            }

            return jsonGeneral;
        }

        private void ServerCommandHandler(IUICommand command)
        {
            ConnectServer();
        }

        private async void ShowUpdateDialog(String version, String updateUrl)
        {
            MessageDialog messageDialog = new MessageDialog("New update found. (" + version + ")");

            messageDialog.Commands.Add(new UICommand("Update") { Id = 0 });
            messageDialog.DefaultCommandIndex = 0;

            Game.Log("Game update dialog opened.");
            var result = await messageDialog.ShowAsync();
            Game.Log("Game update dialog closed.");

            if ((int)result.Id == 0)
            {
                Uri uri = new Uri(updateUrl);
                await Launcher.LaunchUriAsync(uri);
            }
        }

        private void MatchmakingFadedIn1(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchmakingFadedIn1;

            Game.Transition = false;
            SearchMatches();
        }

        private async void SearchMatches()
        {
            TextBlockLine1.Text = "Searching for matches...";
            matches = await DownloadMatchesFile();

            if (matches == null)
            {
                MessageDialog messageDialog = new MessageDialog("Error while searching matches.");
                messageDialog.Commands.Add(new UICommand("Retry", new UICommandInvokedHandler(this.MatchesCommandHandler)));
                messageDialog.DefaultCommandIndex = 0;
                await messageDialog.ShowAsync();

                return;
            }

            qualityMatches = new List<JsonMatch>();

            if (matches.Count > 0)
            {
                TextBlockLine1.Text = "Searching for matches (" + matches.Count + " found)";
                TextBlockLine2.Text = "Checking match quality... (" + qualityMatchesIndex + " of " + matches.Count + ")";

                qualityMatchesIndex = 0;
                CheckMatchesQuality();
            }
            else
            {
                Game.Log("No matches found.");
                CreateMatch();
            }
        }

        private async Task<List<JsonMatch>> DownloadMatchesFile()
        {
            List<JsonMatch> matchesList = null;
            Uri requestUri = new Uri(ServerHost + PathMatches + "?player_id=" + Game.PlayerId);

            HttpBaseProtocolFilter httpProtocolFilter = new HttpBaseProtocolFilter();
            httpProtocolFilter.CacheControl.ReadBehavior = HttpCacheReadBehavior.NoCache;

            HttpClient httpClient = new HttpClient(httpProtocolFilter);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Game");
            httpClient.DefaultRequestHeaders.Add("X-Version", GetAppVersion());
            HttpResponseMessage httpResponse = new HttpResponseMessage();
            string httpResponseBody = String.Empty;

            try
            {
                httpResponse = await httpClient.GetAsync(requestUri);

                if (httpResponse.IsSuccessStatusCode)
                {
                    httpResponseBody = await httpResponse.Content.ReadAsStringAsync();
                    matchesList = JsonConvert.DeserializeObject<List<JsonMatch>>(httpResponseBody);
                }
                else
                {
                    Game.Log("Error downloading matches.json from server. (Code: " + httpResponse.StatusCode + ")");
                }
            }
            catch (Exception e)
            {
                Game.Log("Error downloading matches.json from server.");
            }

            return matchesList;
        }

        private void MatchesCommandHandler(IUICommand command)
        {
            SearchMatches();
        }

        private async void CheckMatchesQuality()
        {
            if (qualityMatchesIndex < matches.Count)
            {
                JsonMatch match = matches[qualityMatchesIndex];
                TextBlockLine2.Text = "Checking match quality... (" + qualityMatchesIndex + " of " + matches.Count + ")";

                await CheckMatchQuality(match);
            }
            else
            {
                Game.Log("No matches with good quality found.");
                CreateMatch();
            }
        }

        private async Task CheckMatchQuality(JsonMatch match)
        {
            try
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource.CancelAfter(2000);

                StreamSocket streamSocket = new StreamSocket();
                streamSocket.Control.QualityOfService = SocketQualityOfService.LowLatency;
                IAsyncAction streamSocketConnectAction = streamSocket.ConnectAsync(new HostName(match.ip_address), match.port.ToString());
                Task streamSocketConnectTask = streamSocketConnectAction.AsTask(cancellationTokenSource.Token);

                await streamSocketConnectTask;

                connection = new Connection(server, streamSocket);
                connection.PingTestReplyReceivedEvent += new PingTestReplyHandler(PingTestReplyReceived);
                connection.SendPingTest();
            }
            catch (Exception e)
            {
                connection = null;
                Game.Log("Ping test to " + match.ip_address + " (" + match.port + ") failed.");

                qualityMatchesIndex++;

                TextBlockLine3.Text = "Error: connection failed.";
                await Task.Delay(ErrorDisplayTime);

                CheckMatchesQuality();
            }
        }

        private void PingTestReplyReceived(object source, PingTestReplyArguments e)
        {
            connection = null;
            qualityMatchesIndex++;
            Game.Log("Ping test time from " + e.HostIpAddress + " (" + e.HostPort + ") -> " + e.PingTime + " milliseconds.");

            TextBlockLine2.Text = "Checking match quality... (" + qualityMatchesIndex + " of " + matches.Count + ")";

            if (e.PingTime < MaxPingTime)
            {
                JsonMatch match = null;

                for (int i = 0; i < matches.Count; i++)
                {
                    if (matches[i].ip_address == e.HostIpAddress && matches[i].port == e.HostPort)
                    {
                        match = matches[i];
                    }
                }

                if (match == null)
                {
                    Game.Log("Match not found.");
                    CheckMatchesQuality();
                }
                else
                {
                    qualityMatches.Add(match);

                    if (Game.State == States.Matchmaking && Game.Transition == false)
                    {
                        JoinMatches();
                    }
                }
            }
            else
            {
                Game.Log("Ping test time from " + e.HostIpAddress + " (" + e.HostPort + ") too high.");
                CheckMatchesQuality();
            }
        }

        private async void CreateMatch()
        {
            host = true;
            Game.Log("Creating match...");

            TextBlockLine1.Text = "Creating match...";
            TextBlockLine2.Text = String.Empty;
            TextBlockLine3.Text = String.Empty;

            if (!server.Listening)
            {
                await server.Start(port);
            }

            server.NewConnectionEvent += new NewConnectionHandler(NewConnection);
            server.LoadMapEvent += new LoadMapHandler(MoveLoadingScreen);
            server.ShowMapEvent += new ShowMapHandler(ShowMap);
            server.ShowCountdownEvent += new ShowCountdownHandler(ShowCountdown);
            server.ShowMatchDetailsEvent += new ShowMatchDetailsHandler(ShowMatchDetails);

            bool matchCreated = await SendCreateMatchRequest();

            if (!matchCreated)
            {
                MessageDialog messageDialog = new MessageDialog("Connection to server failed.");
                messageDialog.Commands.Add(new UICommand("Retry", new UICommandInvokedHandler(this.CreateMatchCommandHandler)));
                messageDialog.DefaultCommandIndex = 0;
                await messageDialog.ShowAsync();

                return;
            }

            Game.Transition = true;
            Game.State = States.MatchDetails;
            Game.Log("Match created.");

            localPlayer = new Player()
            {
                Id = Game.PlayerId,
                Host = true,
                Local = true,
                PingTime = -1,
                Ready = Game.Ready
            };

            players.Add(localPlayer);
            UpdatePlayerList();

            Game.Log("Moving to the match details screen...");

            Storyboard storyboard = (Storyboard)Resources["StoryboardMatchmakingOpacity0"];
            storyboard.Completed += MatchmakingFadedOut1;
            storyboard.Begin();

            serverPingInterval = new Timer(SendPing, null, 0, 5000);
        }

        private string GetLocalIpAddress()
        {
            ConnectionProfile connectionProfile = NetworkInformation.GetInternetConnectionProfile();

            if (connectionProfile?.NetworkAdapter == null)
            {
                return null;
            }

            HostName hostname2 =
                NetworkInformation.GetHostNames()
                    .SingleOrDefault(
                        hn =>
                            hn.IPInformation?.NetworkAdapter != null && hn.IPInformation.NetworkAdapter.NetworkAdapterId
                            == connectionProfile.NetworkAdapter.NetworkAdapterId);

            return hostname2?.CanonicalName;
        }

        private async Task<bool> SendCreateMatchRequest()
        {
            bool success = false;
            string ipAddress = GetLocalIpAddress();

            if (ipAddress == null)
            {
                ipAddress = "0";
                Game.Log("Local IP Address: Not available.");
            }
            else
            {
                Game.Log("Local IP Address: " + ipAddress);
            }

            Uri requestUri = new Uri(
                ServerHost + PathCreateMatch +
                "?player_id=" + Game.PlayerId + "&ip_address=" + ipAddress + "&port=" + port
            );

            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Game");
            httpClient.DefaultRequestHeaders.Add("X-Version", GetAppVersion());
            HttpResponseMessage httpResponse = new HttpResponseMessage();
            string httpResponseBody = String.Empty;

            try
            {
                httpResponse = await httpClient.GetAsync(requestUri);

                if (httpResponse.IsSuccessStatusCode)
                {
                    httpResponseBody = await httpResponse.Content.ReadAsStringAsync();
                    success = true;
                    serverConnected = true;
                }
                else
                {
                    Game.Log("Error sending create match request to server. (Code: " + httpResponse.StatusCode + ")");
                }
            }
            catch (Exception e)
            {
                Game.Log("Error sending create match request to server.");
            }

            return success;
        }

        private void MatchmakingFadedOut1(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchmakingFadedOut1;

            Storyboard storyboard = (Storyboard)Resources["StoryboardMatchOpacity1"];
            storyboard.Completed += MatchDetailsFadedIn1;
            storyboard.Begin();
        }

        private void MatchDetailsFadedIn1(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchDetailsFadedIn1;

            Game.Transition = false;

            Game.Log("Match details is on the screen.");
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;

            if (!serverConnected)
            {
                ConnectionLost();
            }
        }

        private void CreateMatchCommandHandler(IUICommand command)
        {
            CreateMatch();
        }

        private void NewConnection(object source, NewConnectionArguments e)
        {
            e.Connection.PlayerJoinedEvent += new PlayerJoinedHandler(PlayerJoined);
            e.Connection.PlayerLeftEvent += new PlayerLeftHandler(PlayerLeft);
            e.Connection.PlayerPingEvent += new PlayerPingHandler(PlayerPingEventReceived);
            e.Connection.PlayerReadyEvent += new PlayerReadyHandler(PlayerReadyEventReceived);
            e.Connection.PlayerMapObjectsRequestedEvent += new PlayerMapObjectsRequestedHandler(MapObjectsRequested);
            e.Connection.PlayerPositionEvent += new PlayerPositionHandler(PlayerPositionReceived);
            e.Connection.PlayerTargetReachedEvent += new PlayerTargetReachedHandler(PlayerTargetReceived);
            e.Connection.LeaveReplyEvent += new LeaveReplyHandler(LeaveReplyReceived);
        }

        private async void SendPing(object state)
        {
            Uri requestUri = new Uri(ServerHost + PathPing + "?player_id=" + Game.PlayerId);

            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Game");
            httpClient.DefaultRequestHeaders.Add("X-Version", GetAppVersion());
            HttpResponseMessage httpResponse = new HttpResponseMessage();
            string httpResponseBody = String.Empty;

            bool success = false;

            try
            {
                httpResponse = await httpClient.GetAsync(requestUri);

                if (httpResponse.IsSuccessStatusCode)
                {
                    httpResponseBody = await httpResponse.Content.ReadAsStringAsync();
                    Game.Log("Ping to matchmaking server sent.");
                    success = true;
                }
                else
                {
                    Game.Log("Error sending ping to matchmaking server. (Code: " + httpResponse.StatusCode + ")");
                }
            }
            catch (Exception e)
            {
                Game.Log("Error sending ping to matchmaking server.");
            }

            if (!success)
            {
                serverPingInterval.Dispose();
                serverConnected = false;

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    // Don't show this message during loading
                    if (!Game.Loading)
                    {
                        ConnectionLost();
                    }
                });
            }
        }

        private async void ConnectionLost()
        {
            await serverConnectionClosedSemaphore.WaitAsync();

            if (serverConnectionClosedLocked)
            {
                serverConnectionClosedSemaphore.Release();
                return;
            }

            for (int i = 0; i < server.Connections.Count; i++)
            {
                server.Connections[i].Close();
            }

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;

            MessageDialog messageDialog = new MessageDialog("Connection to server lost.");
            messageDialog.Commands.Add(new UICommand("Close", new UICommandInvokedHandler(this.ReturnMatchmaking)));
            messageDialog.DefaultCommandIndex = 0;

            Game.Log("Server connection closed dialog opened.");
            await messageDialog.ShowAsync();
            Game.Log("Server connection closed dialog closed.");

            serverConnectionClosedLocked = true;
            serverConnectionClosedSemaphore.Release();
        }

        private async void JoinMatches()
        {
            Game.Log("Joining to match...");

            TextBlockLine2.Text = "Checking match quality (" + qualityMatches.Count + " available)";
            TextBlockLine3.Text = "Establishing connection to host...";

            bool joinedMatch = false;

            foreach (JsonMatch qualityMatch in qualityMatches)
            {
                joinedMatch = await JoinMatch(qualityMatch);

                if (!joinedMatch)
                {
                    TextBlockLine3.Text = "Error: connection failed.";
                }
            }

            if (!joinedMatch)
            {
                await Task.Delay(500);
                CreateMatch();
            }
        }

        private async Task<bool> JoinMatch(JsonMatch qualityMatch)
        {
            bool success = false;

            try
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource.CancelAfter(2000);

                StreamSocket streamSocket = new StreamSocket();
                streamSocket.Control.QualityOfService = SocketQualityOfService.LowLatency;
                IAsyncAction streamSocketConnectAction = streamSocket.ConnectAsync(new HostName(qualityMatch.ip_address), qualityMatch.port.ToString());
                Task streamSocketConnectTask = streamSocketConnectAction.AsTask(cancellationTokenSource.Token);

                await streamSocketConnectTask;

                connection = new Connection(null, streamSocket);
                connection.JoinMatchEvent += new JoinMatchHandler(JoinMatchEventReceived);
                connection.PlayerJoinedEvent += new PlayerJoinedHandler(PlayerJoined);
                connection.PlayerLeftEvent += new PlayerLeftHandler(PlayerLeft);
                connection.MatchStateEvent += new MatchStateHandler(MatchStateEventReceived);
                connection.ReadyTimeEvent += new ReadyTimeHandler(ReadyTimeLeftReceived);
                connection.PlayerPingEvent += new PlayerPingHandler(PlayerPingEventReceived);
                connection.PlayerReadyEvent += new PlayerReadyHandler(PlayerReadyEventReceived);
                connection.LoadMapEvent += new LoadMapHandler(MoveLoadingScreen);
                connection.MapObjectsCreatedEvent += new MapObjectsCreatedHandler(MapObjectsCreated);
                connection.PlayerPositionEvent += new PlayerPositionHandler(PlayerPositionReceived);
                connection.TargetPositionEvent += new TargetPositionHandler(TargetPositionReceived);
                connection.PlayerScoreEvent += new PlayerScoreHandler(PlayerScoreReceived);
                connection.MapDataEndEvent += new MapDataEndHandler(MapLoaded);
                connection.ShowMapEvent += new ShowMapHandler(ShowMap);
                connection.MatchCountdownEvent += new MatchCountdownHandler(CountdownReceived);
                connection.MatchStartedEvent += new MatchStartedHandler(MatchStarted);
                connection.MatchTimeLeftEvent += new MatchTimeLeftHandler(MatchTimeLeftReceived);
                connection.PlayerTargetReachedEvent += new PlayerTargetReachedHandler(PlayerTargetReceived);
                connection.MatchEndedEvent += new MatchEndedHandler(MatchEnded);
                connection.LeaveReplyEvent += new LeaveReplyHandler(LeaveReplyReceived);
                connection.LoadMatchDetailsEvent += new LoadMatchDetailsHandler(ReturnMatchDetails);
                connection.ShowMatchDetailsEvent += new ShowMatchDetailsHandler(ShowMatchDetails);

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(Game.PlayerId);
                Tuple<short, byte[]> joinMatchPacket = connection.CreatePacket(Packets.JoinRequest, playerIdBytes);
                await connection.SendPacket(joinMatchPacket);

                success = true;
            }
            catch (Exception e)
            {
                connection = null;
                Game.Log("Error joining to match from " + qualityMatch.ip_address + " (" + qualityMatch.port + ") match.");

                TextBlockLine3.Text = "Error: connection failed.";
                await Task.Delay(ErrorDisplayTime);
            }

            return success;
        }

        private async void JoinMatchEventReceived(object source, JoinMatchArguments e)
        {
            if (e.Code == 0)
            {
                Game.Log("Joined to match.");
            }
            else if (e.Code > 0)
            {
                if (e.Code == 1)
                {
                    Game.Log("Error: player id already used.");
                    TextBlockLine3.Text = "Error: player id already used.";
                }
                else if (e.Code == 2)
                {
                    Game.Log("Error: maximum number of players.");
                    TextBlockLine3.Text = "Error: maximum number of players.";
                }
                else if (e.Code == 3)
                {
                    Game.Log("Error: host left.");
                    TextBlockLine3.Text = "Error: host left.";
                }

                await Task.Delay(ErrorDisplayTime);

                qualityMatchesIndex++;
                CheckMatchesQuality();
            }
        }

        private void PlayerJoined(object source, PlayerJoinedArguments e)
        {
            Game.Log("Player (" + e.PlayerId + ") joined.");

            Player player = new Player()
            {
                Id = e.PlayerId,
                Local = false,
                Host = e.Host,
                Ready = e.Ready
            };

            players.Add(player);
            UpdatePlayerList();

            if (host && Game.State == States.MatchDetails)
            {
                UpdateReadyTimeLeftInterval(60);
            }

            if (Game.State >= States.Loading1)
            {
                playersCarSemaphore.WaitAsync();

                if (player.Car != null)
                {
                    return;
                }

                Car playerCar = new Car(MaxMapWidth / 2, MaxMapHeight / 2);
                player.Car = playerCar;

                Grid playerCarGrid = player.Car.Create(player.Id);
                GridMap.Children.Add(playerCarGrid);

                playersCarSemaphore.Release();
            }
        }

        private void PlayerLeft(object source, PlayerLeftArguments e)
        {
            Game.Log("Player (" + e.PlayerId + ") left.");
            int remoteScore = 0;

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.Id == e.PlayerId)
                {
                    if (player.Car != null && player.Car.Grid != null)
                    {
                        GridMap.Children.Remove(player.Car.Grid);
                    }

                    if (player.Host)
                    {
                        Game.RemoteState = States.Paused;

                        if (readyTimeLeftInterval != null)
                        {
                            readyTimeLeftInterval.Stop();
                        }

                        if (countdownInterval != null)
                        {
                            countdownInterval.Stop();
                        }

                        if (timeLeftInterval != null)
                        {
                            timeLeftInterval.Stop();
                        }

                        // Don't show this message during loading
                        if (!Game.Loading)
                        {
                            HostConnectionClosed(e.ConnectionLost);
                        }
                    }

                    players.Remove(player);
                }
                else
                {
                    if (!player.Local && remoteScore < player.TargetIndex)
                    {
                        remoteScore = player.TargetIndex;
                    }
                }
            }

            UpdatePlayerList();
            TextBlockRemoteScore.Text = remoteScore.ToString();
        }

        private async void HostConnectionClosed(bool connectionLost)
        {
            await connectionClosedSemaphore.WaitAsync();

            if (connectionClosedLocked)
            {
                connectionClosedSemaphore.Release();
                return;
            }

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            MessageDialog messageDialog = null;

            if (connectionLost)
            {
                messageDialog = new MessageDialog("Connection to host lost.");
            }
            else
            {
                messageDialog = new MessageDialog("Host has left the match.");
            }

            messageDialog.Commands.Add(new UICommand("Close", new UICommandInvokedHandler(this.ReturnMatchmaking)));
            messageDialog.DefaultCommandIndex = 0;

            Game.Log("Host connection closed dialog opened.");
            await messageDialog.ShowAsync();
            Game.Log("Host connection closed dialog closed.");

            connectionClosedLocked = true;
            connectionClosedSemaphore.Release();
        }

        private void UpdatePlayerList()
        {
            TextBlockPlayersCount.Text = players.Count + "/16 Players";
            ListViewPlayers.Items.Clear();

            for (int i = 0; i < 14; i++)
            {
                if (i >= players.Count)
                {
                    StackPanel stackPanelEmptyPlayer = new StackPanel();
                    ListViewPlayers.Items.Add(stackPanelEmptyPlayer);
                    continue;
                }

                Player player = players[i];

                StackPanel stackPanelPlayer = new StackPanel()
                {
                    Orientation = Orientation.Horizontal
                };

                TextBlock textBlockPlayerId = new TextBlock()
                {
                    Text = player.Id,
                    Width = 200,
                    FontWeight = FontWeights.Light
                };

                stackPanelPlayer.Children.Add(textBlockPlayerId);

                TextBlock textBlockPing = new TextBlock()
                {
                    FontWeight = FontWeights.Light
                };

                if (!player.Host)
                {
                    if (player.PingTime >= 0)
                    {
                        Run run1 = new Run()
                        {
                            Text = player.PingTime.ToString().PadLeft(3, ' '),
                            FontFamily = new FontFamily("Courier New"),
                            FontSize = 16
                        };

                        Run run2 = new Run()
                        {
                            Text = " ms"
                        };

                        textBlockPing.Text = String.Empty;
                        textBlockPing.Inlines.Add(run1);
                        textBlockPing.Inlines.Add(run2);
                    }

                    if (player.PingTime >= 400)
                    {
                        textBlockPing.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 59, 48));
                    }
                    else if (player.PingTime >= 200)
                    {
                        textBlockPing.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 149, 0));
                    }
                    else
                    {
                        textBlockPing.Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 217, 100));
                    }
                }

                textBlockPing.Width = 200;
                stackPanelPlayer.Children.Add(textBlockPing);

                TextBlock textBlockReady = new TextBlock()
                {
                    FontWeight = FontWeights.Light
                };

                if (player.Ready)
                {
                    textBlockReady.Text = "Ready";
                    textBlockReady.Foreground = new SolidColorBrush(Color.FromArgb(255, 88, 86, 214));
                }
                else
                {
                    if (player.Local)
                    {
                        Run run1 = new Run()
                        {
                            Text = "Press "
                        };

                        Run run2 = new Run()
                        {
                            Text = "R ",
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 88, 86, 214))
                        };

                        Run run3 = new Run()
                        {
                            Text = "if you are ready"
                        };

                        textBlockReady.Inlines.Add(run1);
                        textBlockReady.Inlines.Add(run2);
                        textBlockReady.Inlines.Add(run3);
                    }
                    else
                    {
                        textBlockReady.Text = "";
                    }
                }
                textBlockReady.Width = 300;
                stackPanelPlayer.Children.Add(textBlockReady);
                ListViewPlayers.Items.Add(stackPanelPlayer);
            }
        }

        private void ListViewPlayerListContainerContentChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            StackPanel playerList = (StackPanel)args.Item;

            if (args.ItemIndex % 2 != 0)
            {
                args.ItemContainer.Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
            }
        }

        private void MatchStateEventReceived(object source, MatchStateArguments e)
        {
            Game.RemoteState = e.Code;

            if (Game.RemoteState < States.Loading1 || Game.RemoteState >= States.MatchEnded)
            {
                // Match not started or ended
                Game.Transition = true;
                Game.State = States.MatchDetails;

                localPlayer = new Player()
                {
                    Id = Game.PlayerId,
                    Local = true,
                    Host = false,
                    Ready = Game.Ready
                };

                players.Add(localPlayer);

                Game.Log("Local player created.");
                UpdatePlayerList();

                if (Game.RemoteState == States.MatchEnded)
                {
                    TextBlockMatchStatus.Text = "Match ending...";
                }
                else if (Game.RemoteState == States.CleanupMatchDetails1 || Game.RemoteState == States.CleanupMatchDetails2)
                {
                    TextBlockMatchStatus.Text = "Cleaning up...";
                }

                Storyboard storyboard = (Storyboard)Resources["StoryboardMatchmakingOpacity0"];
                storyboard.Completed += MatchmakingFadedOut2;
                storyboard.Begin();
            }
            else if (e.Code > States.MatchDetails)
            {
                // Match already started
                MoveLoadingScreen(null);
            }
        }

        private void MatchmakingFadedOut2(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchmakingFadedOut2;

            Storyboard storyboard = (Storyboard)Resources["StoryboardMatchOpacity1"];
            storyboard.Completed += MatchDetailsFadedIn2;
            storyboard.Begin();
        }

        private void MatchDetailsFadedIn2(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchDetailsFadedIn2;

            Game.Transition = false;
            Game.Log("Match details is on the screen.");
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
        }

        private void ReadyTimeLeftReceived(object source, ReadyTimeLeftArguments e)
        {
            readyTimeLeftSecondsPassed = (uint)(ReadyTime - e.Seconds);
            UpdateReadyTimeLeftInterval(e.Seconds);
        }

        private async void UpdateReadyTimeLeftInterval(uint secondsLeft)
        {
            UpdateReadyTimeLeft(secondsLeft);
            Game.Log("Time left for match start (" + secondsLeft + " second/s).");

            if (host)
            {
                byte[] readyTimeLeftPayload = BitConverter.GetBytes(secondsLeft);
                Tuple<short, byte[]> readyTimeLeftPacket = server.CreatePacket(Packets.MatchmakingTimeLeft, readyTimeLeftPayload);
                await server.SendPacket(readyTimeLeftPacket);
            }

            if (readyTimeLeftInterval == null)
            {
                readyTimeLeftInterval = new DispatcherTimer()
                {
                    Interval = new TimeSpan(0, 0, 1)
                };

                readyTimeLeftInterval.Tick += async delegate
                {
                    readyTimeLeftSecondsPassed++;

                    if (readyTimeLeftSecondsPassed < ReadyTime)
                    {
                        secondsLeft = ReadyTime - readyTimeLeftSecondsPassed;
                        UpdateReadyTimeLeft(secondsLeft);

                        if (host)
                        {
                            byte[] readyTimeLeftPayload = BitConverter.GetBytes(secondsLeft);
                            Tuple<short, byte[]> readyTimeLeftPacket = server.CreatePacket(Packets.MatchmakingTimeLeft, readyTimeLeftPayload);
                            await server.SendPacket(readyTimeLeftPacket);
                        }
                    }
                    else
                    {
                        readyTimeLeftInterval.Stop();
                        TextBlockMatchStatus.Text = "Match beginning...";

                        if (host)
                        {
                            MoveLoadingScreen(null);
                        }
                    }
                };
            }

            readyTimeLeftInterval.Start();
        }

        private void UpdateReadyTimeLeft(uint secondsLeft)
        {
            Run run1 = new Run()
            {
                Text = "Match beginning in "
            };

            Run run2 = new Run()
            {
                Text = secondsLeft.ToString().PadLeft(2, '0'),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 149, 0)),
                FontFamily = new FontFamily("Courier New")
            };

            TextBlockMatchStatus.Text = String.Empty;
            TextBlockMatchStatus.Inlines.Add(run1);
            TextBlockMatchStatus.Inlines.Add(run2);
        }

        private void PlayerPingEventReceived(object source, PlayerPingArguments e)
        {
            // Game.Log("Ping time from player (" + e.PlayerId + ") -> " + e.PingTime + " milliseconds.");

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.Id == e.PlayerId)
                {
                    player.PingTime = (long) e.PingTime;

                    if (Game.State == States.MatchDetails)
                    {
                        UpdatePlayerList();
                    }

                    break;
                }
            }
        }

        private async void ToggleReadyStatus()
        {
            ushort readyStatus = 0;

            if (Game.Ready)
            {
                Game.Ready = false;
            }
            else
            {
                Game.Ready = true;
                readyStatus = 1;
            }

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.Id == Game.PlayerId)
                {
                    player.Ready = Game.Ready;
                }
            }

            UpdatePlayerList();

            if (host)
            {
                byte[] readyStatusPayload = new byte[17];
                byte[] playerIdPaddedBytes = new byte[15];

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(Game.PlayerId);
                byte[] readyStatusBytes = BitConverter.GetBytes(readyStatus);

                Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                Buffer.BlockCopy(playerIdBytes, 0, readyStatusPayload, 0, playerIdBytes.Length);
                Buffer.BlockCopy(readyStatusBytes, 0, readyStatusPayload, playerIdPaddedBytes.Length, readyStatusBytes.Length);

                Tuple<short, byte[]> readyPacket = server.CreatePacket(Packets.PlayerReady, readyStatusPayload);
                await server.SendPacket(readyPacket);

                server.HostReady(Game.Ready);
            }
            else
            {
                byte[] readyStatusPayload = BitConverter.GetBytes(readyStatus);
                Tuple<short, byte[]> readyPacket = connection.CreatePacket(Packets.ReadyStatus, readyStatusPayload);
                await connection.SendPacket(readyPacket);
            }
        }

        private void PlayerReadyEventReceived(object source, PlayerReadyArguments e)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.Id == e.PlayerId)
                {
                    player.Ready = e.Ready;
                }
            }

            UpdatePlayerList();
        }

        private async void MoveLoadingScreen(object source)
        {
            // Update remote state
            if (!host && source != null)
            {
                Game.RemoteState = States.Loading1;
            }

            // Only during matchmaking or
            // match details screen
            if (Game.State != States.Matchmaking && Game.State != States.MatchDetails)
            {
                return;
            }

            if (readyTimeLeftInterval != null)
            {
                readyTimeLeftInterval.Stop();
            }

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;

            if (host)
            {
                Tuple<short, byte[]> loadMapPacket = server.CreatePacket(Packets.LoadMap, null);
                await server.SendPacket(loadMapPacket);
            }

            // Reset ready status
            for (int i = 0; i < players.Count; i++)
            {
                players[i].Ready = false;
            }

            TextBlockLoading.Text = "Creating game objects...";
            GridProgessBar.Width = 0;

            Storyboard storyboard = null;

            if (Game.State == States.Matchmaking)
            {
                storyboard = (Storyboard)Resources["StoryboardMatchmakingOpacity0"];
            }
            else if (Game.State == States.MatchDetails)
            {
                storyboard = (Storyboard)Resources["StoryboardMatchOpacity0"];
            }
            else if (Game.State == States.Map)
            {
                storyboard = (Storyboard)Resources["StoryboardMapOpacity0"];
            }

            Game.Loading = true;
            Game.Transition = true;
            Game.State = States.Loading1;
            Game.Log("Moving to the map loading screen...");

            storyboard.Completed += ShowLoading1;
            storyboard.Begin();
        }

        private void ShowLoading1(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= ShowLoading1;

            Storyboard storyboard = (Storyboard)Resources["StoryboardLoadingOpacity1"];
            storyboard.Completed += LoadMap;
            storyboard.Begin();
        }

        private void AnimateProgressBar(float currentProgress)
        {
            Storyboard storyboard = new Storyboard();

            DoubleAnimation doubleAnimation1 = new DoubleAnimation()
            {
                To = currentProgress * 10,
                Duration = new TimeSpan(0, 0, 0, 0, 200),
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(doubleAnimation1, GridProgessBar);
            Storyboard.SetTargetProperty(doubleAnimation1, "Width");

            storyboard.Children.Add(doubleAnimation1);
            storyboard.Begin();
        }

        private async void LoadMap(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= LoadMap;

            Game.Transition = false;
            Game.Log("Map loading is on the screen.");

            targets = new List<Target>();

            Car car = new Car(MaxMapWidth / 2, MaxMapHeight / 2);

            if (localPlayer == null)
            {
                localPlayer = new Player()
                {
                    Id = Game.PlayerId,
                    Local = true,
                    Host = false,
                    Car = car
                };

                players.Add(localPlayer);
                Game.Log("Local player created.");
            }
            else
            {
                localPlayer.Car = car;
                localPlayer.Car.X = MaxMapWidth / 2;
                localPlayer.Car.Y = MaxMapHeight / 2;
                localPlayer.Car.Angle = 270F;
            }

            Grid localCarGrid = localPlayer.Car.Create(null);
            GridMap.Children.Insert(GridMap.Children.IndexOf(TextBlockCountdown), localCarGrid);
            Game.Log("Local player added to the map.");

            await playersCarSemaphore.WaitAsync();

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (!player.Local && player.Car == null)
                {
                    Car playerCar = new Car(MaxMapWidth / 2, MaxMapHeight / 2);
                    player.Car = playerCar;

                    Grid playerCarGrid = player.Car.Create(player.Id);
                    GridMap.Children.Add(playerCarGrid);
                    Game.Log("Player (" + player.Id + ") car added to the map.");
                }
            }

            playersCarSemaphore.Release();

            storyboardCountdown = new Storyboard();

            DoubleAnimation doubleAnimation1 = new DoubleAnimation()
            {
                From = 1,
                To = 0,
                Duration = new TimeSpan(0, 0, 1)
            };

            DoubleAnimation doubleAnimation2 = new DoubleAnimation()
            {
                From = 1,
                To = 0,
                Duration = new Duration(new TimeSpan(0, 0, 2))
            };

            DoubleAnimation doubleAnimation3 = new DoubleAnimation()
            {
                From = 1,
                To = 0,
                Duration = new Duration(new TimeSpan(0, 0, 2))
            };

            Storyboard.SetTarget(doubleAnimation1, TextBlockCountdown);
            Storyboard.SetTargetProperty(doubleAnimation1, "Opacity");

            Storyboard.SetTarget(doubleAnimation2, TextBlockCountdown);
            Storyboard.SetTargetProperty(doubleAnimation2, "(TextBlock.RenderTransform).(ScaleTransform.ScaleX)");

            Storyboard.SetTarget(doubleAnimation3, TextBlockCountdown);
            Storyboard.SetTargetProperty(doubleAnimation3, "(TextBlock.RenderTransform).(ScaleTransform.ScaleY)");

            storyboardCountdown.Children.Add(doubleAnimation1);
            storyboardCountdown.Children.Add(doubleAnimation2);
            storyboardCountdown.Children.Add(doubleAnimation3);

            Game.Log("Countdown animation created.");
            AnimateProgressBar(100 / 3);

            if (host)
            {
                Random random = new Random();

                for (int i = 0; i < targetsCount; i++)
                {
                    float randomX = random.Next(100, (int)MaxMapWidth - 100);
                    float randomY = random.Next(100, (int)MaxMapHeight - 100);

                    Target target = new Target(randomX, randomY);
                    targets.Add(target);
                }
            }

            Game.State = States.Loading2;

            if (host)
            {
                if (!serverConnected)
                {
                    ConnectionLost();
                    return;
                }

                Tuple<short, byte[]> mapObjectsCreatedPacket = server.CreatePacket(Packets.MapObjectsCreated, null);
                await server.SendPacket(mapObjectsCreatedPacket);

                MapLoaded(null);
            }
            else if (Game.RemoteState >= States.Loading2)
            {
                Game.Log("Requesting map objects to host...");

                Tuple<short, byte[]> mapObjectsRequestPacket = connection.CreatePacket(Packets.MapObjectsRequest, null);
                await connection.SendPacket(mapObjectsRequestPacket);
            }
            else if (Game.RemoteState == States.MatchEnded)
            {
                ReturnMatchDetails(null);
            }
            else if (Game.RemoteState == States.Paused)
            {
                HostConnectionClosed(true);
            }
        }

        private async void MapObjectsCreated(object source)
        {
            Game.RemoteState = States.Loading2;

            if (Game.State != States.Loading2)
            {
                return;
            }

            Game.Log("Requesting map objects to host...");

            Tuple<short, byte[]> mapObjectsRequestPacket = connection.CreatePacket(Packets.MapObjectsRequest, null);
            await connection.SendPacket(mapObjectsRequestPacket);
        }

        private async void MapObjectsRequested(object source)
        {
            Connection playerConnection = (Connection)source;
            Game.Log("Map objects request from player (" + playerConnection.PlayerId + ").");

            // Only if match already started
            if (Game.State > States.MatchCountdown)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    Player player = players[i];

                    if (playerConnection.PlayerId == player.Id || player.Car == null)
                    {
                        continue;
                    }

                    byte[] playerPayload = new byte[27];
                    byte[] playerIdBytes = Encoding.ASCII.GetBytes(player.Id);
                    byte[] playerIdPaddedBytes = new byte[15];

                    byte[] playerXBytes = BitConverter.GetBytes(player.Car.X);
                    byte[] playerYBytes = BitConverter.GetBytes(player.Car.Y);
                    byte[] playerAngleBytes = BitConverter.GetBytes(player.Car.Angle);

                    Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                    Buffer.BlockCopy(playerIdBytes, 0, playerPayload, 0, playerIdBytes.Length);
                    Buffer.BlockCopy(playerXBytes, 0, playerPayload, playerIdPaddedBytes.Length, playerXBytes.Length);
                    Buffer.BlockCopy(playerYBytes, 0, playerPayload, playerIdPaddedBytes.Length + playerXBytes.Length, playerYBytes.Length);
                    Buffer.BlockCopy(playerAngleBytes, 0, playerPayload, playerIdPaddedBytes.Length + playerXBytes.Length + playerYBytes.Length, playerAngleBytes.Length);

                    Tuple<short, byte[]> positionPacket = playerConnection.CreatePacket(Packets.PlayerPosition, playerPayload);
                    await playerConnection.SendPacket(positionPacket);
                }

                // Time left
                uint secondsLeft = MatchTime - timeLeftSecondsPassed;

                byte[] timeLeftPayload = BitConverter.GetBytes(secondsLeft);
                Tuple<short, byte[]> timeLeftPacket = playerConnection.CreatePacket(Packets.MatchTimeLeft, timeLeftPayload);
                await playerConnection.SendPacket(timeLeftPacket);
            }

            // Targets
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    Target target = targets[i];

                    byte[] targetPayload = new byte[8];
                    byte[] targetXBytes = BitConverter.GetBytes(target.X);
                    byte[] targetYBytes = BitConverter.GetBytes(target.Y);

                    Buffer.BlockCopy(targetXBytes, 0, targetPayload, 0, targetXBytes.Length);
                    Buffer.BlockCopy(targetYBytes, 0, targetPayload, targetXBytes.Length, targetYBytes.Length);

                    Tuple<short, byte[]> targetPacket = playerConnection.CreatePacket(Packets.TargetPosition, targetPayload);
                    await playerConnection.SendPacket(targetPacket);
                }
            }

            // Score
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                byte[] scorePayload = new byte[17];
                byte[] playerIdPaddedBytes = new byte[15];

                byte[] playerIdBytes = Encoding.ASCII.GetBytes(player.Id);
                byte[] targetIndex = BitConverter.GetBytes(player.TargetIndex);

                Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                Buffer.BlockCopy(playerIdBytes, 0, scorePayload, 0, playerIdBytes.Length);
                Buffer.BlockCopy(targetIndex, 0, scorePayload, playerIdPaddedBytes.Length, targetIndex.Length);

                Tuple<short, byte[]> scorePacket = playerConnection.CreatePacket(Packets.PlayerScore, scorePayload);
                await playerConnection.SendPacket(scorePacket);
            }

            // Map end of data
            Tuple<short, byte[]> mapDataEndPacket = playerConnection.CreatePacket(Packets.MapObjectsSent, null);
            await playerConnection.SendPacket(mapDataEndPacket);
        }

        private async void MapLoaded(object source)
        {
            if (host)
            {
                if (!serverConnected)
                {
                    ConnectionLost();
                    return;
                }

                server.HostMapLoaded();
            }
            else
            {
                Game.Log("Map loaded.");
            }

            TextBlockLoading.Text = "Syncing map load with players...";
            AnimateProgressBar((100 / 3) * 2);

            if (host == false)
            {
                Tuple<short, byte[]> mapLoadedPacket = connection.CreatePacket(Packets.MapLoaded, null);
                await connection.SendPacket(mapLoadedPacket);

                if (Game.RemoteState == States.Paused)
                {
                    HostConnectionClosed(true);
                }
                else if (Game.RemoteState >= States.MatchEnded)
                {
                    Game.State = States.MatchEnded;
                    ReturnMatchDetails(null);
                }
                if (Game.RemoteState >= States.Map)
                {
                    ShowMap(null);
                }
            }
        }

        private async void ShowMap(object source)
        {
            if (!host && source != null)
            {
                Game.RemoteState = States.Map;
            }

            if (Game.State != States.Loading2)
            {
                return;
            }

            Game.Transition = true;
            Game.State = States.Map;
            Game.Log("Moving to the map screen...");

            if (host)
            {
                if (!serverConnected)
                {
                    ConnectionLost();
                    return;
                }

                Tuple<short, byte[]> showMapPacket = server.CreatePacket(Packets.ShowMap, null);
                await server.SendPacket(showMapPacket);
            }
            else if (Game.RemoteState == States.Paused)
            {
                HostConnectionClosed(true);
                return;
            }
            else if (Game.RemoteState >= States.MatchEnded)
            {
                ReturnMatchDetails(null);
            }
            else if (Game.RemoteState >= States.MatchCountdown)
            {
                Game.State = Game.RemoteState;
            }

            AnimateProgressBar(100);

            Storyboard loadingStoryboard = (Storyboard)Resources["StoryboardLoadingOpacity0"];
            loadingStoryboard.Completed += LoadingFadedOut1;
            loadingStoryboard.Begin();

            GameLoop();
        }

        private void LoadingFadedOut1(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= LoadingFadedOut1;

            Game.Loading = false;

            if (host)
            {
                if (!serverConnected)
                {
                    ConnectionLost();
                    return;
                }
            }
            else
            {
                if (Game.RemoteState == States.MatchEnded)
                {
                    ReturnMatchDetails(null);
                    return;
                }
                else if (Game.RemoteState == States.Paused)
                {
                    HostConnectionClosed(true);
                    return;
                }
            }

            if (Game.State >= States.MatchStarted)
            {
                ShowGameObjects();
            }

            Storyboard mapStoryboard = (Storyboard)Resources["StoryboardMapOpacity1"];
            mapStoryboard.Completed += MapFadedIn;
            mapStoryboard.Begin();
        }

        private async void MapFadedIn(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MapFadedIn;

            Game.Transition = false;

            if (host)
            {
                if (!serverConnected)
                {
                    ConnectionLost();
                    return;
                }

                server.HostMapShown();
            }
            else
            {
                Game.Log("Map is on the screen.");
            }

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;

            if (host == false)
            {
                Tuple<short, byte[]> mapShownPacket = connection.CreatePacket(Packets.MapShown, null);
                await connection.SendPacket(mapShownPacket);

                if (Game.RemoteState == States.MatchEnded)
                {
                    ReturnMatchDetails(null);
                }
                else if (Game.RemoteState == States.Paused)
                {
                    HostConnectionClosed(true);
                }
                else if (Game.RemoteState == States.MatchStarted || Game.RemoteState == States.MatchEnded)
                {
                    Game.State = Game.RemoteState;

                    if (Game.State == States.MatchStarted)
                    {
                        Game.Log("Car controllable.");
                        carControllable = true;
                    }
                }
            }
        }

        private void ShowCountdown(object source)
        {
            // Update remote state
            if (source != null)
            {
                Game.RemoteState = States.MatchCountdown;
            }

            // Only during map screen
            if (Game.State != States.Map)
            {
                return;
            }

            Game.State = States.MatchCountdown;
            Game.Log("Countdown started.");

            UpdateCountdownInterval(CountdownTime);
        }

        private void CountdownReceived(object source, MatchCountdownArguments e)
        {
            // Update state
            if (Game.State == States.Map) {
                Game.State = States.MatchCountdown;
            }

            countdownSecondsPassed = CountdownTime - e.Seconds;
            UpdateCountdownInterval(e.Seconds);
        }

        private async void UpdateCountdownInterval(uint secondsLeft)
        {
            UpdateCountdown(secondsLeft);
            Game.Log("Countdown (" + secondsLeft + ").");

            if (host)
            {
                byte[] countdownPayload = BitConverter.GetBytes(secondsLeft);
                Tuple<short, byte[]> countdownPacket = server.CreatePacket(Packets.Countdown, countdownPayload);
                await server.SendPacket(countdownPacket);
            }

            if (secondsLeft > 0)
            {
                if (countdownInterval == null)
                {
                    countdownInterval = new DispatcherTimer()
                    {
                        Interval = new TimeSpan(0, 0, 1)
                    };

                    countdownInterval.Tick += async delegate
                    {
                        countdownSecondsPassed++;

                        if (countdownSecondsPassed < CountdownTime)
                        {
                            secondsLeft = CountdownTime - countdownSecondsPassed;
                            UpdateCountdown(secondsLeft);

                            if (host)
                            {
                                byte[] countdownPayload = BitConverter.GetBytes(secondsLeft);
                                Tuple<short, byte[]> countdownPacket = server.CreatePacket(Packets.Countdown, countdownPayload);
                                await server.SendPacket(countdownPacket);
                            }
                        }
                        else
                        {
                            countdownInterval.Stop();

                            if (host)
                            {
                                Game.Log("Countdown ended.");
                                MatchStarted(null);
                            }
                        }
                    };
                }

                countdownInterval.Start();
            }
        }

        private void UpdateCountdown(uint secondsLeft)
        {
            TextBlockCountdown.Text = secondsLeft.ToString();

            if (storyboardCountdown != null)
            {
                storyboardCountdown.Begin();
            }
        }

        private async void MatchStarted(object source)
        {
            // Update remote state
            if (source != null)
            {
                Game.RemoteState = States.MatchStarted;
            }

            // Only during match countdown
            if (Game.State != States.MatchCountdown)
            {
                return;
            }

            Game.State = States.MatchStarted;
            Game.Log("Match started.");

            if (host)
            {
                Tuple<short, byte[]> matchStartedPacket = server.CreatePacket(Packets.MatchStarted, null);
                await server.SendPacket(matchStartedPacket);
            }

            UpdateTimeLeftInterval(MatchTime);
            ShowGameObjects();

            carControllable = true;
            Game.Log("Car controllable.");
        }

        private void ShowGameObjects()
        {
            if (Game.State == States.MatchStarted && targets.Count != 0)
            {
                Grid targetGrid = targets[0].Create();
                GridMap.Children.Add(targetGrid);

                Game.Log("Target (0) added to the map.");
            }

            localPlayer.Car.Show();
            SendPosition();
        }

        private async void GameLoop()
        {
            Game.Log("Game loop created.");

            while (Game.State >= States.Loading2 && Game.State < States.CleanupMatchDetails1)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    Player player = players[i];

                    if (player.Local && player.Car != null)
                    {
                        player.Car.Update(left, up, right, down, MaxMapWidth, MaxMapHeight);

                        if (player.Car.Moving)
                        {
                            CheckTarget();
                            SendPosition();
                        }
                    }

                    if (player.Car != null)
                    {
                        player.Car.Draw();
                    }
                }

                await Task.Delay(33);
            }
        }

        private void MatchTimeLeftReceived(object source, MatchTimeLeftArguments e)
        {
            timeLeftSecondsPassed = MatchTime - e.Seconds;
            UpdateTimeLeftInterval(e.Seconds);
        }

        private async void UpdateTimeLeftInterval(uint secondsLeft)
        {
            UpdateTimeLeft(secondsLeft);
            Game.Log("Time left for match end (" + secondsLeft + " second/s).");

            if (host)
            {
                byte[] timeLeftPayload = BitConverter.GetBytes(secondsLeft);
                Tuple<short, byte[]> timeLeftPacket = server.CreatePacket(Packets.MatchTimeLeft, timeLeftPayload);
                await server.SendPacket(timeLeftPacket);
            }

            if (secondsLeft > 0)
            {
                if (timeLeftInterval == null)
                {
                    timeLeftInterval = new DispatcherTimer()
                    {
                        Interval = new TimeSpan(0, 0, 1)
                    };

                    timeLeftInterval.Tick += async delegate
                    {
                        timeLeftSecondsPassed++;

                        if (timeLeftSecondsPassed >= MatchTime)
                        {
                            timeLeftSecondsPassed = 30;
                            timeLeftInterval.Stop();
                        }

                        if (timeLeftSecondsPassed <= MatchTime)
                        {
                            secondsLeft = MatchTime - timeLeftSecondsPassed;
                            UpdateTimeLeft(secondsLeft);

                            if (host)
                            {
                                byte[] timeLeftPayload = BitConverter.GetBytes(secondsLeft);
                                Tuple<short, byte[]> timeLeftPacket = server.CreatePacket(Packets.MatchTimeLeft, timeLeftPayload);
                                await server.SendPacket(timeLeftPacket);
                            }
                        }

                        if (host && timeLeftSecondsPassed == 30)
                        {
                            MatchTimeEnded();
                        }
                    };
                }

                timeLeftInterval.Start();
            }
        }

        private void UpdateTimeLeft(uint secondsLeft)
        {
            int minutes = (int)secondsLeft / 60;
            uint seconds = 0;

            if (minutes > 0)
            {
                seconds = secondsLeft - ((uint)minutes * 60);
            }
            else
            {
                minutes = 0;
                seconds = secondsLeft;
            }

            TextBlockMinutesLeft.Text = minutes.ToString().PadLeft(2, '0');
            TextBlockSecondsLeft.Text = seconds.ToString().PadLeft(2, '0');
        }

        private async void SendPosition()
        {
            byte[] playerXBytes = BitConverter.GetBytes(localPlayer.Car.X);
            byte[] playerYBytes = BitConverter.GetBytes(localPlayer.Car.Y);
            byte[] playerAngleBytes = BitConverter.GetBytes(localPlayer.Car.Angle);

            if (host)
            {
                byte[] playerPayload = new byte[27];
                byte[] playerIdBytes = Encoding.ASCII.GetBytes(Game.PlayerId);
                byte[] playerIdPaddedBytes = new byte[15];

                Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                Buffer.BlockCopy(playerIdPaddedBytes, 0, playerPayload, 0, playerIdPaddedBytes.Length);
                Buffer.BlockCopy(playerXBytes, 0, playerPayload, playerIdPaddedBytes.Length, playerXBytes.Length);
                Buffer.BlockCopy(playerYBytes, 0, playerPayload, playerIdPaddedBytes.Length + playerXBytes.Length, playerYBytes.Length);
                Buffer.BlockCopy(playerAngleBytes, 0, playerPayload, playerIdPaddedBytes.Length + playerXBytes.Length + playerYBytes.Length, playerAngleBytes.Length);

                Tuple<short, byte[]> positionPacket = server.CreatePacket(Packets.PlayerPosition, playerPayload);
                await server.SendPacket(positionPacket);
            }
            else
            {
                byte[] positionPayload = new byte[12];

                Buffer.BlockCopy(playerXBytes, 0, positionPayload, 0, playerXBytes.Length);
                Buffer.BlockCopy(playerYBytes, 0, positionPayload, playerXBytes.Length, playerYBytes.Length);
                Buffer.BlockCopy(playerAngleBytes, 0, positionPayload, playerXBytes.Length + playerYBytes.Length, playerAngleBytes.Length);

                Tuple<short, byte[]> positionPacket = connection.CreatePacket(Packets.Position, positionPayload);
                await connection.SendPacket(positionPacket);
            }
        }

        public void PlayerPositionReceived(object source, PlayerPositionArguments e)
        {
            Player player = null;

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Id == e.PlayerId)
                {
                    player = players[i];
                    break;
                }
            }

            if (player != null)
            {
                if (player.Car != null)
                {
                    player.Car.X = e.X;
                    player.Car.Y = e.Y;
                    player.Car.Angle = e.Angle;

                    if (player.Car.Grid != null && player.Car.Grid.Opacity == 0)
                    {
                        player.Car.Show();
                    }
                }
            }
        }

        private async void CheckTarget()
        {
            if (targetIndex >= targets.Count || Game.State == States.MatchEnded)
            {
                return;
            }

            Target target = targets[targetIndex];

            if (localPlayer.Car.X >= (target.X - 25) && localPlayer.Car.X <= (target.X + 25) &&
                localPlayer.Car.Y >= (target.Y - 25) && localPlayer.Car.Y <= (target.Y + 25))
            {
                Game.Log("Target (" + targetIndex + ") reached.");

                GridMap.Children.Remove(target.Grid);
                targetIndex++;

                if ((!matchTimeEnded && Game.State != States.MatchEnded) && targetIndex < targets.Count)
                {
                    Grid targetGrid = targets[targetIndex].Create();
                    GridMap.Children.Add(targetGrid);

                    Game.Log("Target (" + targetIndex + ") added to the map.");
                }

                if (host)
                {
                    localPlayer.TargetIndex = targetIndex;
                    TextBlockLocalScore.Text = targetIndex.ToString();

                    byte[] targetReachedPayload = new byte[17];

                    byte[] playerIdBytes = Encoding.ASCII.GetBytes(Game.PlayerId);
                    byte[] playerIdPaddedBytes = new byte[15];
                    byte[] targetIndexBytes = BitConverter.GetBytes(targetIndex);

                    Buffer.BlockCopy(playerIdBytes, 0, playerIdPaddedBytes, 0, playerIdBytes.Length);

                    Buffer.BlockCopy(playerIdPaddedBytes, 0, targetReachedPayload, 0, playerIdPaddedBytes.Length);
                    Buffer.BlockCopy(targetIndexBytes, 0, targetReachedPayload, playerIdPaddedBytes.Length, targetIndexBytes.Length);

                    Tuple<short, byte[]> targetReachedPacket = server.CreatePacket(Packets.PlayerTargetReached, targetReachedPayload);
                    await server.SendPacket(targetReachedPacket);

                    if (localPlayer.TargetIndex == targets.Count || matchTimeEnded)
                    {
                        CheckScore();
                    }
                }
                else
                {
                    Tuple<short, byte[]> targetReachedPacket = connection.CreatePacket(Packets.TargetReached, null);
                    await connection.SendPacket(targetReachedPacket);
                }
            }
        }

        private void PlayerTargetReceived(object source, PlayerTargetReachedArguments e)
        {
            if (Game.State != States.MatchStarted)
            {
                return;
            }

            Game.Log("Player (" + e.PlayerId + ") has reached target (" + e.TargetIndex + ").");

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.Id == e.PlayerId)
                {
                    player.TargetIndex = e.TargetIndex;

                    if (player.Local)
                    {
                        TextBlockLocalScore.Text = e.TargetIndex.ToString();
                    }
                    else if (e.TargetIndex > remoteTargetIndex)
                    {
                        remoteTargetIndex = e.TargetIndex;
                        TextBlockRemoteScore.Text = remoteTargetIndex.ToString();
                    }

                    if (host)
                    {
                        if (player.TargetIndex == targets.Count || matchTimeEnded)
                        {
                            CheckScore();
                        }
                    }

                    break;
                }
            }
        }

        private void TargetPositionReceived(object source, TargetPositionArguments e)
        {
            Target target = new Target(e.X, e.Y);
            targets.Add(target);
        }

        private void PlayerScoreReceived(object source, PlayerScoreArguments e)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.Id == e.PlayerId)
                {
                    player.TargetIndex = e.Score;

                    if (player.TargetIndex > remoteTargetIndex)
                    {
                        remoteTargetIndex = player.TargetIndex;
                        TextBlockRemoteScore.Text = player.TargetIndex.ToString();
                    }
                }
            }
        }

        private void MatchTimeEnded()
        {
            matchTimeEnded = true;
            CheckScore();
        }

        private Player GetWinner()
        {
            Player winner = null;

            int maxScore = 0;
            int scoreFound = 0;

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];

                if (player.TargetIndex > maxScore)
                {
                    winner = player;

                    scoreFound = 1;
                    maxScore = player.TargetIndex;
                }
                else if (player.TargetIndex == maxScore)
                {
                    winner = null;

                    scoreFound++;
                }
            }

            return winner;
        }

        private void CheckScore()
        {
            Player player = GetWinner();

            if (player != null)
            {
                MatchEnded(null);
            }
        }

        private async void MatchEnded(object source)
        {
            await matchEndedSemaphore.WaitAsync();

            if (matchEndedLocked)
            {
                return;
            }

            // Update match status to
            // players joining
            if (source != null)
            {
                Game.RemoteState = States.MatchEnded;

                if (Game.State == States.MatchDetails)
                {
                    TextBlockMatchStatus.Text = "Match ending...";
                }
            }

            // Only if match has started
            if (Game.State != States.MatchStarted)
            {
                return;
            }

            Game.State = States.MatchEnded;
            Game.Log("Match ended.");

            if (host)
            {
                Tuple<short, byte[]> matchEndedPacket = server.CreatePacket(Packets.MatchEnded, null);
                await server.SendPacket(matchEndedPacket);
            }

            carControllable = false;
            Game.Log("Car not controllable.");

            if (timeLeftInterval != null)
            {
                timeLeftInterval.Stop();
            }

            Player player = GetWinner();

            textBlockCountdown = new TextBlock()
            {
                FontSize = 56,
                FontWeight = FontWeights.Light,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            GridMap.Children.Add(textBlockCountdown);

            ScaleTransform scaleTransfrom = new ScaleTransform()
            {
                ScaleX = 0,
                ScaleY = 0
            };

            textBlockCountdown.RenderTransform = scaleTransfrom;

            Storyboard storyboard = new Storyboard();

            DoubleAnimation doubleAnimation1 = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new TimeSpan(0, 0, 0, 0, 100)
            };

            DoubleAnimation doubleAnimation2 = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new Duration(new TimeSpan(0, 0, 0, 0, 200))
            };

            DoubleAnimation doubleAnimation3 = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new Duration(new TimeSpan(0, 0, 0, 0, 200))
            };
            Storyboard.SetTarget(doubleAnimation1, textBlockCountdown);
            Storyboard.SetTargetProperty(doubleAnimation1, "Opacity");

            Storyboard.SetTarget(doubleAnimation2, textBlockCountdown);
            Storyboard.SetTargetProperty(doubleAnimation2, "(Grid.RenderTransform).(ScaleTransform.ScaleX)");

            Storyboard.SetTarget(doubleAnimation3, textBlockCountdown);
            Storyboard.SetTargetProperty(doubleAnimation3, "(Grid.RenderTransform).(ScaleTransform.ScaleY)");

            storyboard.Children.Add(doubleAnimation1);
            storyboard.Children.Add(doubleAnimation2);
            storyboard.Children.Add(doubleAnimation3);

            if (player != null)
            {
                if (player.Local)
                {
                    Run run1 = new Run()
                    {
                        Text = "YOU",
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 122, 255))
                    };

                    textBlockCountdown.Inlines.Add(run1);
                }
                else
                {
                    Run run2 = new Run()
                    {
                        Text = player.Id.ToUpper(),
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 59, 58))
                    };

                    textBlockCountdown.Inlines.Add(run2);
                }
            }
            else
            {
                Run run3 = new Run()
                {
                    Text = "UNKNOWN",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 59, 58))
                };

                textBlockCountdown.Inlines.Add(run3);
            }

            Run run4 = new Run()
            {
                Text = " WINS!"
            };

            if (player != null)
            {
                if (player.Local)
                {
                    run4.Text = " WON!";
                }
            }

            textBlockCountdown.Inlines.Add(run4);

            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                GridMap.Children.Remove(targets[i].Grid);
            }

            storyboard.Begin();
            storyboard.Completed += async delegate
            {
                if (Game.State == States.MatchEnded)
                {
                    if (!host && Game.RemoteState != States.MatchEnded)
                    {
                        return;
                    }

                    await Task.Delay(3000);
                }
                else
                {
                    return;
                }

                matchEndedLocked = true;
                matchEndedSemaphore.Release();

                if (host)
                {
                    ReturnMatchDetails(null);
                }
            };
        }

        private void Cleanup()
        {
            Game.Ready = false;

            readyTimeLeftInterval = null;
            readyTimeLeftSecondsPassed = 0;
            countdownInterval = null;
            countdownSecondsPassed = 0;
            matchTimeEnded = false;
            timeLeftInterval = null;
            timeLeftSecondsPassed = 0;
            targetIndex = 0;
            remoteTargetIndex = 0;
            matchEndedLocked = false;

            if (host)
            {
                server.Ready = false;
                server.MapLoaded = false;
                server.MapShown = false;

                server.PlayersReadyLocked = false;
                server.PlayersMapLoadedLocked = false;
                server.PlayersMapShownLocked = false;

                for (int i = 0; i < server.Connections.Count; i++)
                {
                    Connection serverConnection = server.Connections[i];

                    serverConnection.MapLoaded = false;
                    serverConnection.MapShown = false;
                    serverConnection.TargetIndex = 0;
                }
            }

            if (textBlockCountdown != null)
            {
                GridMap.Children.Remove(textBlockCountdown);
            }

            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    GridMap.Children.Remove(targets[i].Grid);
                    Game.Log("Target (" + i + ") deleted from the map.");
                }

                targets.Clear();
                Game.Log("Targets deleted.");
            }

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
                player.TargetIndex = 0;

                if (player.Car != null && player.Car.Grid != null)
                {
                    GridMap.Children.Remove(player.Car.Grid);
                    player.Car = null;

                    Game.Log("Player (" + player.Id + ") car deleted from the map.");
                }
            }

            UpdateTimeLeft(MatchTime);
            TextBlockLocalScore.Text = "0";
            TextBlockRemoteScore.Text = "0";

            UpdatePlayerList();
            TextBlockMatchStatus.Text = "Waiting for players";
        }

        private async void SendLeaveRequest(object sender, BackRequestedEventArgs e)
        {
            if (leavingMatch)
            {
                return;
            }

            leavingMatch = true;

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            leaveMatchTimer = new Timer(LeaveRequestTimedOut, true, 5 * 1000, Timeout.Infinite);

            Game.Log("Leaving match...");

            if (host)
            {
                if (server.Connections.Count > 0)
                {
                    Tuple<short, byte[]> leaveMatchPacket = server.CreatePacket(Packets.LeaveRequest, null);
                    await server.SendPacket(leaveMatchPacket);

                    Game.Log("Leave request sent.");
                }
                else
                {
                    if (leaveMatchTimer != null)
                    {
                        leaveMatchTimer.Dispose();
                    }

                    ReturnMatchmaking(null);
                }
            }
            else
            {
                connection.LeaveRequestSent = true;

                Tuple<short, byte[]> leaveRequestPacket = connection.CreatePacket(Packets.LeaveRequest, null);
                await connection.SendPacket(leaveRequestPacket);
            }
        }

        private void LeaveReplyReceived(object source)
        {
            bool leaveRequestAccepted = true;

            for (int i = 0; i < server.Connections.Count; i++)
            {
                Connection serverConnection = server.Connections[i];

                if (serverConnection.Joined && !serverConnection.LeaveRequestAccepted)
                {
                    leaveRequestAccepted = false;
                }
            }

            if (leaveRequestAccepted)
            {
                LeaveRequestCompleted();
            }
        }

        private async void LeaveRequestTimedOut(object state)
        {
            if (leaveMatchTimer != null)
            {
                if (state != null)
                {
                    bool timedOut = (bool)state;

                    if (timedOut)
                    {
                        Game.Log("Leave request not accepted, leaving match...");
                    }
                }
            }

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                LeaveRequestCompleted();
            });
        }

        private async void LeaveRequestCompleted()
        {
            await leaveMatchSemaphore.WaitAsync();

            if (leaveMatchLocked)
            {
                leaveMatchSemaphore.Release();
                return;
            }

            // Stop timer
            if (leaveMatchTimer != null)
            {
                leaveMatchTimer.Dispose();
            }

            // Close connections
            if (host)
            {
                for (int i = 0; i < server.Connections.Count; i++)
                {
                    server.Connections[i].Close();
                }
            }
            else
            {
                connection.Close();
                Game.RemoteState = States.Paused;
            }

            ReturnMatchmaking(null);

            leaveMatchLocked = true;
            leaveMatchSemaphore.Release();
        }

        private async void ReturnMatchmaking(IUICommand command)
        {
            await Task.Delay(1000);

            Storyboard storyboard = null;

            if (Game.State == States.MatchDetails)
            {
                storyboard = (Storyboard)Resources["StoryboardMatchOpacity0"];
            }
            else if (Game.State == States.Loading1 || Game.State == States.Loading2)
            {
                storyboard = (Storyboard)Resources["StoryboardLoadingOpacity0"];
            }
            else if (Game.State == States.Map || Game.State == States.MatchStarted || Game.State == States.MatchCountdown || Game.State == States.MatchEnded)
            {
                storyboard = (Storyboard)Resources["StoryboardMapOpacity0"];
            }

            Game.Transition = true;
            Game.State = States.CleanupMatchmaking;
            Game.Log("Moving to the matchmaking cleanup loading screen...");

            storyboard.Completed += ShowCleanupMatchmaking;
            storyboard.Begin();
        }

        private void ShowCleanupMatchmaking(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= ShowCleanupMatchmaking;

            TextBlockLoading.Text = "Cleaning up...";
            GridProgessBar.Width = 0;

            Storyboard storyboard1 = (Storyboard)Resources["StoryboardLoadingOpacity1"];
            storyboard1.Completed += CleanupMatchmaking;
            storyboard1.Begin();
        }

        private void CleanupMatchmaking(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= CleanupMatchmaking;

            Game.Transition = false;

            if (serverPingInterval != null)
            {
                serverPingInterval.Dispose();
            }

            if (server != null)
            {
                server.NewConnectionEvent -= new NewConnectionHandler(NewConnection);
                server.LoadMapEvent -= new LoadMapHandler(MoveLoadingScreen);
                server.ShowMapEvent -= new ShowMapHandler(ShowMap);
                server.ShowCountdownEvent -= new ShowCountdownHandler(ShowCountdown);
                server.ShowMatchDetailsEvent -= new ShowMatchDetailsHandler(ShowMatchDetails);

                server.Stop();
            }

            if (readyTimeLeftInterval != null)
            {
                readyTimeLeftInterval.Stop();
            }

            if (timeLeftInterval != null)
            {
                timeLeftInterval.Stop();
            }

            if (countdownInterval != null)
            {
                countdownInterval.Stop();
            }

            serverConnectionClosedLocked = false;
            connectionClosedLocked = false;
            leaveMatchLocked = false;
            leavingMatch = false;

            Cleanup();
            localPlayer = null;

            players.Clear();
            Game.Log("Players deleted.");

            host = false;

            TextBlockLine1.Text = "Searching for matches...";
            TextBlockLine2.Text = String.Empty;
            TextBlockLine3.Text = String.Empty;

            UpdatePlayerList();
            AnimateProgressBar(100);

            Game.Transition = true;
            Game.State = States.MatchDetails;

            Storyboard storyboard1 = (Storyboard)Resources["StoryboardLoadingOpacity0"];
            storyboard1.Completed += ShowMatchmaking;
            storyboard1.Begin();
        }

        private void ShowMatchmaking(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= ShowMatchmaking;

            Game.Loading = false;
            Game.Log("Moving to the matchmaking screen...");

            Storyboard storyboard1 = (Storyboard)Resources["StoryboardMatchmakingOpacity1"];
            storyboard1.Completed += MatchmakingFadedIn;
            storyboard1.Begin();
        }

        private void MatchmakingFadedIn(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchmakingFadedIn;

            Game.Transition = false;
            Game.State = States.Matchmaking;
            Game.Log("Matchmaking is on the screen.");

            if (host)
            {
                server.PlayersCleanupCompletedLocked = false;
            }

            SearchMatches();
        }

        private async void ReturnMatchDetails(object source)
        {
            // Update match status to
            // players joining
            if (source != null)
            {
                Game.RemoteState = States.CleanupMatchDetails1;

                if (Game.State == States.MatchDetails)
                {
                    TextBlockMatchStatus.Text = "Cleaning up...";
                }
            }

            // Only if match has ended
            if (Game.State != States.MatchEnded)
            {
                return;
            }

            if (timeLeftInterval != null)
            {
                timeLeftInterval.Stop();
            }

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;

            Game.Loading = true;
            Game.Transition = true;
            Game.State = States.CleanupMatchDetails1;
            Game.Log("Moving to the match details cleanup loading screen...");

            if (host)
            {
                Tuple<short, byte[]> loadMatchDetailsPacket = server.CreatePacket(Packets.LoadMatchDetails, null);
                await server.SendPacket(loadMatchDetailsPacket);
            }

            Storyboard storyboard = (Storyboard)Resources["StoryboardMapOpacity0"];
            storyboard.Completed += ShowCleanupMatchDetails;
            storyboard.Begin();
        }

        private void ShowCleanupMatchDetails(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= ShowCleanupMatchDetails;

            TextBlockLoading.Text = "Cleaning up...";
            GridProgessBar.Width = 0;

            Storyboard storyboard1 = (Storyboard)Resources["StoryboardLoadingOpacity1"];
            storyboard1.Completed += CleanupMatchDetails;
            storyboard1.Begin();
        }

        private async void CleanupMatchDetails(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= CleanupMatchDetails;

            Game.Transition = false;
            Game.Log("Cleanup loading (moving to match details) is on the screen.");

            Cleanup();
            UpdatePlayerList();

            Game.State = States.CleanupMatchDetails2;

            AnimateProgressBar(50);
            TextBlockLoading.Text = "Syncing cleanup with players...";

            if (host)
            {
                if (!serverConnected)
                {
                    ConnectionLost();
                    return;
                }

                server.HostCleanupCompleted();
            }
            else
            {
                Tuple<short, byte[]> cleanupCompletedPacket = connection.CreatePacket(Packets.CleanupCompleted, null);
                await connection.SendPacket(cleanupCompletedPacket);

                if (Game.RemoteState == States.MatchDetails)
                {
                    ShowMatchDetails(null);
                }
                else if (Game.RemoteState == States.Paused)
                {
                    HostConnectionClosed(true);
                }
            }
        }

        private async void ShowMatchDetails(object source)
        {
            // Update remote state
            if (!host && source != null)
            {
                Game.RemoteState = States.MatchDetails;
            }

            // Only during syncing cleanup with players
            // loading screen
            if (Game.State != States.CleanupMatchDetails2)
            {
                return;
            }

            if (host)
            {
                Tuple<short, byte[]> showMatchDetailsdPacket = server.CreatePacket(Packets.ShowMatchDetails, null);
                await server.SendPacket(showMatchDetailsdPacket);
            }

            AnimateProgressBar(100);

            Game.Transition = true;
            Game.State = States.MatchDetails;

            Storyboard storyboard1 = (Storyboard)Resources["StoryboardLoadingOpacity0"];
            storyboard1.Completed += CleanupMatchDetailsFadedOut;
            storyboard1.Begin();
        }

        private void CleanupMatchDetailsFadedOut(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= CleanupMatchDetailsFadedOut;

            Game.Loading = false;
            Game.Log("Moving to the match details screen...");

            Storyboard storyboard1 = (Storyboard)Resources["StoryboardMatchOpacity1"];
            storyboard1.Completed += MatchDetailsFadedIn;
            storyboard1.Begin();
        }

        private void MatchDetailsFadedIn(object sender, object e)
        {
            Storyboard oldStoryboard = (Storyboard)sender;
            oldStoryboard.Completed -= MatchDetailsFadedIn;

            Game.Transition = false;
            Game.Log("Match details is on the screen.");

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;

            if (host)
            {
                server.PlayersCleanupCompletedLocked = false;

                if (server.Connections.Count > 0)
                {
                    UpdateReadyTimeLeftInterval(60);
                }
            }
            else if (Game.RemoteState > States.MatchDetails)
            {
                MoveLoadingScreen(null);
            }
        }
    }
}