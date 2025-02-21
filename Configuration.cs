using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBot;

internal static class Configuration
{
    //GRTUSDT,RSRUSDT,STXUSDT
    internal static int totalMessages = 1000, limit = 100, Leverage = 10;
    internal static decimal InitialBalance = 10m;
    internal static readonly string[] PAIR_BLACKLIST = ["LITUSDT", "ORBUSDT", "MEMEUSDT", "SLPUSDT", "IOSTUSDT", "BATUSDT", "RONUSDT", "XRDUSDT", "ZEUSUSDT", "MERLUSDT", "POWRUSDT", "NOTUSDT", "PORTALUSDT", "MOVRUSDT", "ORBSUSDT"];
    internal static string BinanceApiUrl = "https://fapi.binance.com/fapi/v1/klines";
    internal static decimal STOP_LOSS_PERCENTAGE = 40m;

    internal static bool IsFilterDateFilter = true;
    internal static DateTime FROM_DATE = new DateTime(2024, 11, 1);
    internal static DateTime TO_DATE = new DateTime(2024, 11, 30);

    public static string Interval = "5m";
        }