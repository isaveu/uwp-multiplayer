using System;

namespace Game
{
    public class Events
    {
        public class NewConnectionArguments : EventArgs
        {
            public Connection Connection { get; set; }
            public long PingTime { get; set; }

            public NewConnectionArguments(Connection connection)
            {
                this.Connection = connection;
            }
        }

        public class PingTestReplyArguments : EventArgs
        {
            public string HostIpAddress { get; set; }
            public int HostPort { get; set; }
            public long PingTime { get; set; }

            public PingTestReplyArguments(string hostIpAddress, int hostPort, long pingTime)
            {
                this.HostIpAddress = hostIpAddress;
                this.HostPort = hostPort;
                this.PingTime = pingTime;
            }
        }

        public class JoinMatchArguments : EventArgs
        {
            public int Code { get; set; }

            public JoinMatchArguments(int code)
            {
                this.Code = code;
            }
        }

        public class PlayerJoinedArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public bool Host { get; set; }
            public bool Ready { get; set; }

            public PlayerJoinedArguments(string playerId, bool host, bool ready)
            {
                this.PlayerId = playerId;
                this.Host = host;
                this.Ready = ready;
            }
        }

        public class PlayerLeftArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public bool ConnectionLost { get; set; }

            public PlayerLeftArguments(string playerId, bool connectionLost)
            {
                this.PlayerId = playerId;
                this.ConnectionLost = connectionLost;
            }
        }

        public class MatchStateArguments : EventArgs
        {
            public ushort Code { get; set; }

            public MatchStateArguments(ushort code)
            {
                this.Code = code;
            }
        }

        public class ReadyTimeLeftArguments : EventArgs
        {
            public uint Seconds { get; set; }

            public ReadyTimeLeftArguments(uint seconds)
            {
                this.Seconds = seconds;
            }
        }

        public class PlayerPingArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public ulong PingTime { get; set; }

            public PlayerPingArguments(string playerId, ulong pingTime)
            {
                this.PlayerId = playerId;
                this.PingTime = pingTime;
            }
        }

        public class PlayerReadyArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public bool Ready { get; set; }

            public PlayerReadyArguments(string playerId, bool ready)
            {
                this.PlayerId = playerId;
                this.Ready = ready;
            }
        }

        public class PlayerPositionArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Angle { get; set; }

            public PlayerPositionArguments(string playerId, float x, float y, float angle)
            {
                this.PlayerId = playerId;
                this.X = x;
                this.Y = y;
                this.Angle = angle;
            }
        }

        public class TargetPositionArguments : EventArgs
        {
            public float X { get; set; }
            public float Y { get; set; }

            public TargetPositionArguments(float x, float y)
            {
                this.X = x;
                this.Y = y;
            }
        }

        public class PlayerScoreArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public ushort Score { get; set; }

            public PlayerScoreArguments(string playerId, ushort score)
            {
                this.PlayerId = playerId;
                this.Score = score;
            }
        }

        public class MatchCountdownArguments : EventArgs
        {
            public uint Seconds { get; set; }

            public MatchCountdownArguments(uint seconds)
            {
                this.Seconds = seconds;
            }
        }

        public class MatchTimeLeftArguments : EventArgs
        {
            public uint Seconds { get; set; }

            public MatchTimeLeftArguments(uint seconds)
            {
                this.Seconds = seconds;
            }
        }

        public class PlayerTargetReachedArguments : EventArgs
        {
            public string PlayerId { get; set; }
            public ushort TargetIndex { get; set; }

            public PlayerTargetReachedArguments(string playerId, ushort targetIndex)
            {
                this.PlayerId = playerId;
                this.TargetIndex = targetIndex;
            }
        }
    }
}
