using BitfinexAPIConnector.Abstract;
using BitfinexAPIConnector.Models;
using RestSharp;
using System.Globalization;
using System.Text.Json;

namespace BitfinexAPIConnector;

public class TestConnector : ITestConnector
{
    public event Action<Trade> NewBuyTrade;
    public event Action<Trade> NewSellTrade;
    public event Action<Candle> CandleSeriesProcessing;


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
        var trades = JsonSerializer.Deserialize<List<List<object>>>(response.Content)
            .Select(c => new Trade
            {
                Pair = pair,
                Id = c[0].ToString(),
                Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[1].ToString())),
                Amount = Math.Abs(DecimalParseFromResponse(c[2])),
                Side = DecimalParseFromResponse(c[2]) > 0 ? "buy" : "sell",
                Price = DecimalParseFromResponse(c[3]),
            })
            .ToList();
        return trades;
    }
    #endregion

    public Task SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
    {
        throw new NotImplementedException();
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
}
