namespace BitfinexAPIConnector;

public class MessageCandle
{
    public string @event { get; set; }
    public string channel { get; set; }
    public string key { get; set; }

}

public class MessageTrade
{
    public string @event { get; set; }
    public string channel { get; set; }
    public string symbol { get; set; }
}

public class MessageUnsubscribe
{
    public string @event { get; set; }
    public int chanId { get; set; }
}
