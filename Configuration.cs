using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBot;

internal static class Configuration
{
    
    internal static int totalMessages = 5000, limit = 100, Leverage = 30;
    internal static decimal InitialBalance =4300;

    internal static readonly bool IsUsingBlacklist=false;
    internal static readonly string[] PAIR_BLACKLIST = ["LITUSDT", "ORBUSDT", "MEMEUSDT", "SLPUSDT",
     "IOSTUSDT", "BATUSDT", "RONUSDT", "XRDUSDT", "ZEUSUSDT", "MERLUSDT",
     "POWRUSDT", "NOTUSDT", "PORTALUSDT", "MOVRUSDT", "ORBSUSDT","GRTUSDT","STXUSDT"];
//,"ICPUSDT","RSRUSDT"
    internal static readonly string[] PAIR_WHITELIST = ["MTLUSDT", "UNIUSDT","BTCUSDT","ROSEUSDT"];

    internal static string BinanceApiUrl = "https://fapi.binance.com/fapi/v1/klines";
    internal static decimal STOP_LOSS_PERCENTAGE = 20m;
    public static decimal CommissionRate = 0.0002m; // 0.02% por trade

    internal static bool IsFilterDateFilter = true;
    internal static DateTime FROM_DATE = new DateTime(2023, 11, 1);
    internal static DateTime TO_DATE = new DateTime(2025, 2, 22,15,17,0);

    public static string Interval = "5m";



    public static decimal RiskPercentage =     0.2M;

}