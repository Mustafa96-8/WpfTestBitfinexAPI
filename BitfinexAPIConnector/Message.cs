namespace BitfinexAPIConnector;
public partial class TestConnector
{
    internal class Message
    {
        public string @event { get; set; }
        public string channel { get; set; }
        public string key { get; set; }

    }
}

