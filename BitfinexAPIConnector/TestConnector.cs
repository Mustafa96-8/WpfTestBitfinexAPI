using BitfinexAPIConnector.Abstract;
using BitfinexAPIConnector.Models;
using RestSharp;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BitfinexAPIConnector;

public class TestConnector : ITestConnector,IDisposable
{
    public event Action<Trade> NewBuyTrade;
    public event Action<Trade> NewSellTrade;
    public event Action<Candle> CandleSeriesProcessing;

    private ClientWebSocket ws;
    private Dictionary<int, string> candlesConnectionsId = new();
    private Dictionary<int, string> tradesConnectionsId = new();
    private CancellationTokenSource cts;
    private bool _isListening = false;

    public TestConnector()
    {
        ws = new ClientWebSocket();
        cts = new CancellationTokenSource();
        Task task1 = Task.Run(() => OnListen());
    }


    private async Task ConnectToWebSocket()
    {
        Uri uri = new Uri("wss://api-pub.bitfinex.com/ws/2");
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(uri, cts.Token);
            Console.WriteLine("WebSocket подключен");
        }
        catch
        {
            await Task.Delay(5000);
        }
    }

    private static decimal DecimalParseFromResponse(object row)
        => decimal.Parse(row.ToString(), new NumberFormatInfo() { NumberDecimalSeparator = "." });


    #region REST
    private static async Task<RestResponse> RestRequestGet(string url)
    {
        var options = new RestClientOptions(url);
        var client = new RestClient(options);
        var request = new RestRequest("");
        request.AddHeader("accept", "application/json");
        return await client.GetAsync(request);
    }
    public async Task<IEnumerable<Candle>> GetCandleSeriesAsync(string pair, int periodInSec, DateTimeOffset? from, DateTimeOffset? to = null, long? count = 0)
    {
        int periodInMin = (int)double.Round(periodInSec / 60d);
        string endTimestr = to.HasValue ? $"&end={((DateTimeOffset)to).ToUnixTimeMilliseconds()}" : "";
        string startTimestr = from.HasValue ? $"&start={((DateTimeOffset)from).ToUnixTimeMilliseconds()}" : "";

        var response = await RestRequestGet($"https://api-pub.bitfinex.com/v2/candles/trade%3A{periodInMin}m%3A{pair}/hist?sort=1{startTimestr}{endTimestr}&limit={count}");
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
        var candles = JsonSerializer.Deserialize<List<List<JsonElement>>>(response.Content)
            .Select(c => CreateCandleFromJSON(c.AsEnumerable().ToList(),pair))
            .ToList();
        return candles;
    }

    public async Task<IEnumerable<Trade>> GetNewTradesAsync(string pair, int maxCount)
    {
        var response = await RestRequestGet($"https://api-pub.bitfinex.com/v2/trades/{pair}/hist?limit={maxCount}&sort=-1");

        if(response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
        var trades = JsonSerializer.Deserialize<List<List<JsonElement>>>(response.Content)
            .Select(c => CreateTradeFromJSON(c.AsEnumerable().ToList(), pair))
            .ToList();
        return trades;
    }
    #endregion


    #region WebSocket
    private async void OnListen()
    {              
        while (true)
            {

            if(ws != null &&( ws.State!=WebSocketState.Open||ws.State!=WebSocketState.Connecting))
            {
                await ConnectToWebSocket();
            }

            if(ws != null && ws.State == WebSocketState.Open)
            {
                var buffer = new Byte[16384];
                while(ws.State == WebSocketState.Open)
                {
                    Array.Clear(buffer, 0, buffer.Length);

                    var response = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if(response.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("WebSocket closed by server.");
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, response.Count);
                    if(message.StartsWith("{"))
                    {
                        var responseJson = JsonSerializer.Deserialize<JsonElement>(message);

                        if(responseJson.TryGetProperty("event", out JsonElement eventType))
                        {
                            string eventStr = eventType.GetString();

                            if(eventStr == "info")
                            {
                                continue;
                            }
                            var chanId = JsonSerializer.Deserialize<int>(responseJson.GetProperty("chanId"));
                            
                            if(!responseJson.TryGetProperty("channel", out JsonElement channelType))
                            {
                                continue;
                            }
                            string channelTypeString = channelType.ToString();
                            if(eventStr == "subscribed")
                            {
                                if(channelTypeString == "trades")
                                {
                                    var symbol = responseJson.GetProperty("symbol").ToString();
                                    tradesConnectionsId.Add(chanId, symbol);
                                }
                                else if(channelTypeString == "candles")
                                {
                                    var key = responseJson.GetProperty("key").ToString().Split(':')[2];
                                    candlesConnectionsId.Add(chanId, key);
                                }
                            }
                            if(eventStr == "unsubscribed")
                            {
                                
                                if(channelTypeString == "trades")
                                {
                                    var symbol = responseJson.GetProperty("symbol").ToString();
                                    tradesConnectionsId.Remove(chanId);
                                }
                                else if(channelTypeString == "candles")
                                {
                                    var key = responseJson.GetProperty("key").ToString();
                                    candlesConnectionsId.Remove(chanId);
                                }
                            }
                        }
                    }
                    else
                    {
                        var responseArray = JsonSerializer.Deserialize<List<JsonElement>>(message);
                        var chanId = responseArray[0].GetInt32();
                        string key;
                        if(responseArray[1].ValueKind != JsonValueKind.Array)
                        { 
                            continue;
                        }

                        if(candlesConnectionsId.TryGetValue(chanId, out key))
                        {
                            var candleDatas = responseArray[1].Deserialize<List<JsonElement>>();
                            if(candleDatas != null && candleDatas.Count > 0)
                            {
                                if(candleDatas[0].ValueKind != JsonValueKind.Array)
                                {
                                    var candle = CreateCandleFromJSON(candleDatas, key);
                                    CandleSeriesProcessing?.Invoke(candle);
                                }
                                else
                                {
                                    var candles = candleDatas
                                        .Select(c => CreateCandleFromJSON(c.EnumerateArray().ToList(), key))
                                        .ToList();

                                    foreach(var candle in candles)
                                    {
                                        CandleSeriesProcessing?.Invoke(candle);
                                    }
                                }
                            }

                        }
                        else if(tradesConnectionsId.TryGetValue(chanId, out key))
                        {
                            var tradeDatas = responseArray[1].Deserialize<List<JsonElement>>();
                            if(tradeDatas!=null&& tradeDatas.Count > 0)
                            {
                                if(tradeDatas[0].ValueKind != JsonValueKind.Array)
                                {
                                    var trade = CreateTradeFromJSON(tradeDatas, key);
                                    if(trade.Side == "buy")
                                        NewBuyTrade?.Invoke(trade);
                                    else
                                        NewSellTrade?.Invoke(trade);
                                }
                                else
                                {
                                    var trades = tradeDatas
                                        .Select(c => CreateTradeFromJSON(c.EnumerateArray().ToList(), key))
                                        .ToList();
                                    foreach(var trade in trades)
                                    {
                                        if(trade.Side == "buy")
                                            NewBuyTrade?.Invoke(trade);
                                        else
                                            NewSellTrade?.Invoke(trade);
                                    }
                                }
                            }
                                
                        }
                    }
                }
            }
            //Если соединение разорвалось но подписки ещё остались
            if(candlesConnectionsId.Count > 0 || tradesConnectionsId.Count > 0)
            {
                await ConnectToWebSocket();
            }
            
            /*    Console.WriteLine($"Ошибка при прослушивании WebSocket: {ex.Message}");
                //Если соединение разорвалось но подписки ещё остались
                if(candlesConnectionsId.Count > 0 || tradesConnectionsId.Count > 0)
                {
                    _isListening = false;
                    await ConnectToWebSocket();
                }
            */
        }

    }

    private static Candle CreateCandleFromJSON(List<JsonElement> c,string pair)
    {
        return new Candle
        {
            Pair = pair,
            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(c[0].GetInt64()),
            OpenPrice = DecimalParseFromResponse(c[1]),
            ClosePrice = DecimalParseFromResponse(c[2]),
            HighPrice = DecimalParseFromResponse(c[3]),
            LowPrice = DecimalParseFromResponse(c[4]),
            TotalVolume = DecimalParseFromResponse(c[5]),
            TotalPrice = DecimalParseFromResponse(c[5]) * DecimalParseFromResponse(c[2]),
        };

    }                                   
    private static Trade CreateTradeFromJSON(List<JsonElement> c,string pair)
    {
        return new Trade
        {
            Pair = pair,
            Id = c[0].ToString(),
            Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[1].ToString())),
            Amount = Math.Abs(DecimalParseFromResponse(c[2])),
            Side = DecimalParseFromResponse(c[2]) > 0 ? "buy" : "sell",
            Price = DecimalParseFromResponse(c[3]),
        };

    }


    public void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
    {

        int periodInMinute = periodInSec / 60;
        if(ws.State == WebSocketState.Open)
        {
            var msg = new MessageCandle
            {
                @event = "subscribe",
                channel = "candles",
                key = $"trade:{periodInMinute}m:{pair}"
            };
            if(candlesConnectionsId.ContainsValue(msg.key))
            {
                return;
            }
            string jsonString = JsonSerializer.Serialize(msg);
            var binaryJS = Encoding.UTF8.GetBytes(jsonString);
            ws.SendAsync(new ArraySegment<byte>(binaryJS), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    public void SubscribeTrades(string pair, int maxCount = 100)
    {
        if(ws.State == WebSocketState.Open)
        {
            var msg = new MessageTrade
            {
                @event = "subscribe",
                channel = "trades",
                symbol = pair,
            };
            if(tradesConnectionsId.ContainsValue(msg.symbol))
            {
                return;
            }
            string jsonString = JsonSerializer.Serialize(msg);
            var binaryJS = Encoding.UTF8.GetBytes(jsonString);
            ws.SendAsync(new ArraySegment<byte>(binaryJS), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    public void UnsubscribeCandles(string pair)
    {
        if(ws.State == WebSocketState.Open)
        {
            if(!candlesConnectionsId.ContainsValue(pair))
            {
                return;
            }
            int chanId = candlesConnectionsId.FirstOrDefault(u => u.Value == pair).Key;
            var msg = new MessageUnsubscribe
            {
                @event = "unsubscribe",
                chanId = chanId
            };
            string jsonString = JsonSerializer.Serialize(msg);
            var binaryJS = Encoding.UTF8.GetBytes(jsonString);
            ws.SendAsync(new ArraySegment<byte>(binaryJS), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    public void UnsubscribeTrades(string pair)
    {
        if(ws.State == WebSocketState.Open)
        {
            if(!tradesConnectionsId.ContainsValue(pair))
            {
                return;
            }
            int chanId = tradesConnectionsId.FirstOrDefault(u => u.Value == pair).Key;
            var msg = new MessageUnsubscribe
            {
                @event = "unsubscribe",
                chanId = chanId
            };
            string jsonString = JsonSerializer.Serialize(msg);
            var binaryJS = Encoding.UTF8.GetBytes(jsonString);
            ws.SendAsync(new ArraySegment<byte>(binaryJS), WebSocketMessageType.Text, true, cts.Token);
        }
    }
    #endregion

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        cts.Cancel(false);
        cts.Dispose();
        ws.Dispose();
    }
}
