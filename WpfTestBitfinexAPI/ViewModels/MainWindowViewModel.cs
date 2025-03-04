using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
            {
                SetProperty(ref countOfItemsInRequest, intVal);
            }
        }
    }

    private string pare;
    private string Pare
    {
        get => pare;
        set
        {
            SetProperty(ref pare, value);
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

    public DateTimeOffset dateTimeTo = DateTimeOffset.UtcNow;
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


    private async Task GetCandleREST()
    {
        candleRESTList.Clear();
        candleRESTList = (await testConnector.GetCandleSeriesAsync(pare, 60, from:dateTimeFrom,to:dateTimeTo, count:countOfItemsInRequest)).ToObservableCollection();
        OnPropertyChanged(nameof(candleRESTList));
    }
    private async Task GetTradeREST()
    {
        tradeRESTList.Clear();
        tradeRESTList = (await testConnector.GetNewTradesAsync(pare, maxCount: (int)countOfItemsInRequest)).ToObservableCollection();
        OnPropertyChanged(nameof(tradeRESTList));
    }

    public MainWindowViewModel()
    {
        candleRESTList = new ObservableCollection<Candle>();
        candleWSList = new ObservableCollection<Candle>();
        tradeRESTList = new ObservableCollection<Trade>();
        tradeWSList = new ObservableCollection<Trade>();
        GetCandle = new RelayCommand(async execute => await GetCandleREST(), canExecute => true);
        GetTrade = new RelayCommand(async execute => await GetTradeREST(), canExecute => true);

        this.testConnector = new TestConnector();
    }

}
