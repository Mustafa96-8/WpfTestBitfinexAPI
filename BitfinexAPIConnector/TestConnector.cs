using BitfinexAPIConnector.Abstract;
using BitfinexAPIConnector.Models;
using RestSharp;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BitfinexAPIConnector;

public class TestConnector : ITestConnector
{
    public event Action<Trade> NewBuyTrade;
    public event Action<Trade> NewSellTrade;
    public event Action<Candle> CandleSeriesProcessing;



    private ClientWebSocket ws;
    private Dictionary<int, string> candlesChatId = new();
    private Dictionary<int, string> tradesChatId = new();
    private CancellationTokenSource cts;

    public TestConnector()
    {
        Uri uri = new Uri("wss://api-pub.bitfinex.com/ws/2");
        ws = new ClientWebSocket();
        cts = new CancellationTokenSource();
        ws.ConnectAsync(uri, cts.Token);
        var thread1 = new Thread(OnListen);
        thread1.Start();
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
        var candles = JsonSerializer.Deserialize<List<List<object>>>(response.Content)
            .Select(c => new Candle
            {
                Pair = pair,
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[0].ToString())),
                OpenPrice = DecimalParseFromResponse(c[1]),
                ClosePrice = DecimalParseFromResponse(c[2]),
                HighPrice = DecimalParseFromResponse(c[3]),
                LowPrice = DecimalParseFromResponse(c[4]),
                TotalVolume = DecimalParseFromResponse(c[5]),
                TotalPrice = DecimalParseFromResponse(c[5]) * DecimalParseFromResponse(c[2]),
            })
            .ToList();
        return candles;
    }

    public async Task<IEnumerable<Trade>> GetNewTradesAsync(string pair, int maxCount)
    {
        var response = await RestRequestGet($"https://api-pub.bitfinex.com/v2/trades/{pair}/hist?limit={maxCount}&sort=-1");
        var numberFormat = new NumberFormatInfo() { NumberDecimalSeparator = "." };
        var trades = JsonSerializer.Deserialize<List<List<decimal>>>(response.Content)
            .Select(c => new Trade
            {
                Pair = pair,
                Id = c[0].ToString(),
                Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[1])),
                Amount = Math.Abs(c[2]),
                Side =c[2] > 0 ? "buy" : "sell",
                Price = c[3],
            })
            .ToList();
        return trades;
    }
    #endregion


    #region WebSoket
    
    private async void OnListen()
    {
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
                        if(eventStr == "subscribed")
                        {
                            var chanId = Convert.ToInt32(responseJson.GetProperty("chanId"));
                            if(responseJson.TryGetProperty("channel", out JsonElement channelType))
                            {
                                if(channelType.GetString() == "trades")
                                {
                                    var symbol = responseJson.GetProperty("symbol").ToString();
                                    tradesChatId.Add(chanId, symbol);
                                }
                                else if(channelType.ToString() == "candles")
                                {
                                    var key = responseJson.GetProperty("key").ToString();
                                    candlesChatId.Add(chanId, key);
                                }
                            }
                        }
                        if(eventStr == "unsubscribed")
                        {
                            var chanId = Convert.ToInt32(responseJson.GetProperty("chanId"));
                            if(responseJson.TryGetProperty("channel", out JsonElement channelType))
                            {
                                if(channelType.GetString() == "trades")
                                {
                                    var symbol = responseJson.GetProperty("symbol").ToString();
                                    tradesChatId.Remove(chanId);
                                }
                                else if(channelType.ToString() == "candles")
                                {
                                    var key = responseJson.GetProperty("key").ToString();
                                    candlesChatId.Remove(chanId);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var responseArray = JsonSerializer.Deserialize<List<JsonElement>>(message);
                    var chanId = responseArray[0].GetInt32();
                    string key;

                    if(candlesChatId.TryGetValue(chanId, out key))
                    {
                        if(responseArray[1].ValueKind == JsonValueKind.Array)
                        {
                            var candleDatas = responseArray[1].Deserialize<List<JsonElement>>();
                            if(candleDatas[0].ValueKind != JsonValueKind.Array)
                            {
                                var c = candleDatas;
                                Candle candle = new Candle
                                {
                                    Pair = key,
                                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[0].ToString())),
                                    OpenPrice = DecimalParseFromResponse(c[1]),
                                    ClosePrice = DecimalParseFromResponse(c[2]),
                                    HighPrice = DecimalParseFromResponse(c[3]),
                                    LowPrice = DecimalParseFromResponse(c[4]),
                                    TotalVolume = DecimalParseFromResponse(c[5]),
                                    TotalPrice = DecimalParseFromResponse(c[5]) * DecimalParseFromResponse(c[2]),

                                };
                                CandleSeriesProcessing?.Invoke(candle);
                            }
                            else
                            {
                                var candles = candleDatas.Select(c => new Candle
                                {
                                    Pair = key,
                                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[0].ToString())),
                                    OpenPrice = DecimalParseFromResponse(c[1]),
                                    ClosePrice = DecimalParseFromResponse(c[2]),
                                    HighPrice = DecimalParseFromResponse(c[3]),
                                    LowPrice = DecimalParseFromResponse(c[4]),
                                    TotalVolume = DecimalParseFromResponse(c[5]),
                                    TotalPrice = DecimalParseFromResponse(c[5]) * DecimalParseFromResponse(c[2]),
                                });
                                foreach(var candle in candles)
                                {
                                    CandleSeriesProcessing?.Invoke(candle);
                                }
                            }
                        }

                    }
                    else if(tradesChatId.TryGetValue(chanId, out key))
                    {
                        if(responseArray[1].ValueKind == JsonValueKind.Array)
                        {
                            var tradeDatas = responseArray[1].Deserialize<List<JsonElement>>();
                            if(tradeDatas[0].ValueKind != JsonValueKind.Array)
                            {
                                var c = tradeDatas;
                                Trade trade = new Trade
                                {
                                    Pair = key,
                                    Id = c[0].ToString(),
                                    Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[1].ToString())),
                                    Amount = Math.Abs(DecimalParseFromResponse(c[2])),
                                    Side = DecimalParseFromResponse(c[2]) > 0 ? "buy" : "sell",
                                    Price = DecimalParseFromResponse(c[3]),
                                };
                                if(trade.Side == "buy")
                                    NewBuyTrade?.Invoke(trade);
                                else
                                    NewSellTrade?.Invoke(trade);
                            }
                            else
                            {
                                var trades = tradeDatas.Select(c => new Trade
                                {
                                    Pair = key,
                                    Id = c[0].ToString(),
                                    Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[1].ToString())),
                                    Amount = Math.Abs(DecimalParseFromResponse(c[2])),
                                    Side = DecimalParseFromResponse(c[2]) > 0 ? "buy" : "sell",
                                    Price = DecimalParseFromResponse(c[3]),
                                });
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
    }


    public void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
    {
        int periodInMinute = periodInSec / 60;
        if(ws.State == WebSocketState.Open)
        {
            var msg = new Message
            {
                @event = "subscribe",
                channel = "candles",
                key = $"trade:{periodInMinute}m:{pair}"
            };
            string jsonString = JsonSerializer.Serialize(msg);
            var binaryJS = Encoding.UTF8.GetBytes(jsonString);
            ws.SendAsync(new ArraySegment<byte>(binaryJS), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    public void SubscribeTrades(string pair, int maxCount = 100)
    {
        throw new NotImplementedException();
    }

    public void UnsubscribeCandles(string pair)
    {
        throw new NotImplementedException();
    }

    public void UnsubscribeTrades(string pair)
    {
        throw new NotImplementedException();
    }
    #endregion
}
