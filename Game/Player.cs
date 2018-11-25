namespace Game
{
    public class Player
    {
        public string Id { get; set; }
        public bool Local { get; set; }
        public bool Host { get; set; }

        public long PingTime { get; set; }
        public bool Ready { get; set; }

        public Car Car { get; set; }
        public ushort TargetIndex { get; set; }
    }
}
