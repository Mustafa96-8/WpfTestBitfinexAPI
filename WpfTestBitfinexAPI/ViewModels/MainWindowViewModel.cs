using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BitfinexAPIConnector;
using BitfinexAPIConnector.Abstract;
using BitfinexAPIConnector.Models;
using WpfTestBitfinexAPI.Extensions;

namespace WpfTestBitfinexAPI.ViewModels;

public class MainWindowViewModel : BaseViewModel
{
    private ITestConnector testConnector { get; init; }

    public ObservableCollection<Candle> candleRESTList { get; private set; }
    public ObservableCollection<Candle> candleWSList { get; private set; }
    public ObservableCollection<Trade> tradeRESTList { get; private set; }
    public ObservableCollection<Trade> tradeWSList { get; private set; }

    public ICommand GetCandle { get; private set; }
    public ICommand GetTrade { get; private set; }

    public ICommand SubscribeCandle { get; private set; }
    public ICommand SubscribeTrade { get; private set; }
    public ICommand UnsubscribeCandle { get; private set; }
    public ICommand UnsubscribeTrade { get; private set; }

    private long countOfItemsInRequest = 0;
    public string CountOfItemsInRequest
    {
        get => countOfItemsInRequest.ToString();
        set
        {
            long intVal;
            if(long.TryParse(value, out intVal))
                SetProperty(ref countOfItemsInRequest, intVal);
        }
    }

    private string pair;
    public string Pair
    {
        get => pair;
        set
        {
            SetProperty(ref pair, value);
        }
    }

    private int periodInSec;
    public string PeriodInSec
    {
        get => periodInSec.ToString();
        set
        {
            int intVal;
            if(int.TryParse(value, out intVal))
                SetProperty(ref periodInSec, intVal);
        }
    } 

    public DateTimeOffset dateTimeFrom;
    public string DateTimeFrom
    {
        get => dateTimeFrom.ToString();
        set
        {
            DateTimeOffset dateTimeOffset;
            if(DateTimeOffset.TryParse(value,out dateTimeOffset))
                SetProperty(ref dateTimeFrom, dateTimeOffset);
        }
    }

    public DateTimeOffset dateTimeTo;
    public string DateTimeTo
    {
        get => dateTimeTo.ToString();
        set
        {
            DateTimeOffset dateTimeOffset;
            if(DateTimeOffset.TryParse(value, out dateTimeOffset))
                SetProperty(ref dateTimeTo, dateTimeOffset);
        }
    }


    #region REST
    private async Task GetCandleREST()
    {
        candleRESTList.Clear();
        candleRESTList = (await testConnector.GetCandleSeriesAsync(pair, periodInSec, from:dateTimeFrom,to:dateTimeTo, count:countOfItemsInRequest)).ToObservableCollection();
        OnPropertyChanged(nameof(candleRESTList));
    }
    private async Task GetTradeREST()
    {
        tradeRESTList.Clear();
        tradeRESTList = (await testConnector.GetNewTradesAsync(pair, maxCount: (int)countOfItemsInRequest)).ToObservableCollection();
        OnPropertyChanged(nameof(tradeRESTList));
    }
    #endregion

    #region WebSocket
    private void SubscribeCandleWS()
    {
        testConnector.SubscribeCandles(pair, periodInSec, dateTimeFrom, dateTimeTo, countOfItemsInRequest);
    }

    private void SubscribeTradeWS()
    {
        testConnector.SubscribeTrades(pair,(int)countOfItemsInRequest);
    }


    private void UnsubscribeCandleWS()
    {
        testConnector.UnsubscribeCandles(pair);
    }

    private void UnsubscribeTradeWS()
    {
        testConnector.UnsubscribeTrades(pair);
    }

    #endregion
    #region Actions
    private void CandleHandler(Candle candle)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if(candleWSList.ToArray().Length >= countOfItemsInRequest)
            {
                candleWSList.RemoveAt(0);
            }
            candleWSList.Add(candle);
            OnPropertyChanged(nameof(candleWSList));
        });
    }

    private void BuyTradeHandler(Trade trade)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if(tradeWSList.ToArray().Length >= countOfItemsInRequest)
            {
                tradeWSList.RemoveAt(0);
            }
            tradeWSList.Add(trade);
            OnPropertyChanged(nameof(tradeWSList));
        });
    }
    private void SellTradeHandler(Trade trade)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if(tradeWSList.Count >= countOfItemsInRequest)
            {
                tradeWSList.RemoveAt(0);
            }
            tradeWSList.Add(trade);
            OnPropertyChanged(nameof(tradeWSList));
        });
    }
    #endregion




    public MainWindowViewModel()
    {
        dateTimeTo = DateTimeOffset.Now.AddDays(-1); 
        candleRESTList = new ObservableCollection<Candle>();
        candleWSList = new ObservableCollection<Candle>();
        tradeRESTList = new ObservableCollection<Trade>();
        tradeWSList = new ObservableCollection<Trade>();
        GetCandle = new RelayCommand(async execute => await GetCandleREST(), canExecute => true);
        GetTrade = new RelayCommand(async execute => await GetTradeREST(), canExecute => true);
        SubscribeCandle = new RelayCommand( execute => SubscribeCandleWS(), canExecute => true);
        SubscribeTrade = new RelayCommand( execute => SubscribeTradeWS(), canExecute => true);
        UnsubscribeCandle = new RelayCommand( execute => UnsubscribeCandleWS(), canExecute => true);
        UnsubscribeTrade = new RelayCommand( execute => UnsubscribeTradeWS(), canExecute => true);

        this.testConnector = new TestConnector();
        testConnector.CandleSeriesProcessing += CandleHandler;
        testConnector.NewSellTrade += SellTradeHandler;
        testConnector.NewBuyTrade += BuyTradeHandler;

        periodInSec = 60;
    }

}
