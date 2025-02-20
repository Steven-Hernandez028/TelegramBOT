using WTelegram;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OfficeOpenXml;
using Binance.Net;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects;
using Binance.Net.Clients;

class Program
{
    static BinanceRestClient binanceClient = new BinanceRestClient();
    static BinanceSocketClient binanceSocket = new BinanceSocketClient();

    static async Task Main(string[] args)
    {
        Client client = new Client(23436543,"64c48edd4b9f0363de544f5bc7ec194a");
        await client.LoginUserIfNeeded();
        
        var signals = await ProcessTelegramChannel(client, "Whales signals");
        SaveToExcel(signals);
        await BacktestSignals(signals);
    }

    static async Task<List<Signal>> ProcessTelegramChannel(Client client, string channelName)
    {
        var signals = new List<Signal>();
        var dialogs = await client.Messages_GetAllDialogs();
        
        foreach (var chat in dialogs.chats.Values)
        {
            if (chat.Title == channelName)
            {
                int offsetId = 0;
                const int batchSize = 100;
                List<Message> messagesBatch;

                do
                {
                    var messages = await client.Messages_GetHistory(chat, offsetId, 0, 0, batchSize);
                    messagesBatch = messages.Messages.OfType<TL.Message>().Cast<Message>().ToList();
                    
                    foreach (var msg in messagesBatch)
                    {
                        var signal = ParseSignal(msg.message, msg.id, msg.Date.ToUniversalTime());
                        if (signal != null && ValidateSymbol(signal.Symbol))
                            signals.Add(signal);
                    }
                    
                    offsetId = messagesBatch.LastOrDefault()?.id ?? 0;
                    await Task.Delay(500); // Rate limit protection
                    
                } while (messagesBatch.Count == batchSize);
            }
        }
        return signals;
    }

    static Signal ParseSignal(string messageText, int messageId, DateTime utcDate)
    {
        var signal = new Signal 
        { 
            MessageID = messageId,
            UTC_DateTime = utcDate,
            Symbol = ExtractSymbol(messageText)
        };

        // Expresión regular mejorada para capturar todos los componentes
        var pattern = @"(?<direction>LONG|SHORT).*Entry:\s*(?<entry>\d+\.\d+).*TP1:\s*(?<tp1>\d+\.\d+)(\s*\/\s*TP2:\s*(?<tp2>\d+\.\d+))?.*SL:\s*(?<sl>\d+\.\d+)";
        var match = System.Text.RegularExpressions.Regex.Match(messageText, pattern);

        if (match.Success)
        {
            signal.Direction = match.Groups["direction"].Value;
            signal.Entry = decimal.Parse(match.Groups["entry"].Value);
            signal.TP1 = decimal.Parse(match.Groups["tp1"].Value);
            signal.SL = decimal.Parse(match.Groups["sl"].Value);

            if (match.Groups["tp2"].Success)
                signal.TP2 = decimal.Parse(match.Groups["tp2"].Value);
        }

        return signal.Entry == 0 ? null : signal;
    }

    static string ExtractSymbol(string text)
    {
        var symbolMatch = System.Text.RegularExpressions.Regex.Match(text, @"([A-Z]+)USDT\.?P?");
        return symbolMatch.Success ? $"{symbolMatch.Groups[1].Value}USDT" : null;
    }

    static bool ValidateSymbol(string symbol)
    {
        var exchangeInfo = binanceClient.GetExchangeInfo();
        return exchangeInfo.Success && 
               exchangeInfo.Data.Symbols.Any(s => 
                   s.Name == symbol && 
                   s.ContractType == ContractType.Perpetual);
    }

    static async Task BacktestSignals(List<Signal> signals)
    {
        var results = new List<BacktestResult>();
        
        foreach (var signal in signals)
        {
            try
            {
                var klines = await GetHistoricalData(signal);
                if (klines.Any())
                {
                    var result = ProcessKlines(signal, klines);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error procesando señal {signal.MessageID}: {ex.Message}");
            }
            await Task.Delay(100); // Control de rate limit
        }
        
        SaveBacktestResults(results);
    }

    static async Task<List<IBinanceKline>> GetHistoricalData(Signal signal)
    {
        var klines = new List<IBinanceKline>();
        var startTime = signal.UTC_DateTime;
        DateTime? endTime = null;
        int maxTries = 3;

        for (int i = 0; i < maxTries; i++)
        {
            var response = await binanceClient.GetKlinesAsync(
                symbol: signal.Symbol,
                interval: KlineInterval.FiveMinutes,
                startTime: startTime,
                endTime: endTime,
                limit: 1000);

            if (response.Success)
            {
                klines.AddRange(response.Data.Reverse());
                if (response.Data.Length < 1000) break;
                endTime = response.Data.Min(k => k.OpenTime) - TimeSpan.FromMinutes(5);
            }
            else
            {
                await Task.Delay(1000 * (i + 1)); // Backoff exponencial
            }
        }
        return klines;
    }

    static BacktestResult ProcessKlines(Signal signal, List<IBinanceKline> klines)
    {
        var result = new BacktestResult { Signal = signal };
        bool positionOpened = false;
        decimal extremePrice = signal.Direction == "LONG" ? decimal.MaxValue : decimal.MinValue;

        foreach (var kline in klines.OrderBy(k => k.OpenTime))
        {
            if (!positionOpened)
            {
                if (CheckEntryCondition(signal, kline))
                {
                    positionOpened = true;
                    result.EntryTime = kline.OpenTime;
                }
            }
            else
            {
                extremePrice = signal.Direction == "LONG" 
                    ? Math.Min(extremePrice, kline.LowPrice)
                    : Math.Max(extremePrice, kline.HighPrice);

                if (CheckExitCondition(signal, kline, ref result))
                    break;
            }
        }

        result.LowestAfterEntry = signal.Direction == "LONG" ? extremePrice : 0;
        result.HighestAfterEntry = signal.Direction == "SHORT" ? extremePrice : 0;
        return result;
    }

    static bool CheckEntryCondition(Signal signal, IBinanceKline kline)
    {
        return signal.Direction == "LONG" 
            ? kline.LowPrice <= signal.Entry 
            : kline.HighPrice >= signal.Entry;
    }

    static bool CheckExitCondition(Signal signal, IBinanceKline kline, ref BacktestResult result)
    {
        if (signal.Direction == "LONG")
        {
            if (kline.HighPrice >= signal.TP1)
            {
                result.SetOutcome("TP1", kline.OpenTime);
                return true;
            }
            if (signal.TP2.HasValue && kline.HighPrice >= signal.TP2.Value)
            {
                result.SetOutcome("TP2", kline.OpenTime);
                return true;
            }
            if (kline.LowPrice <= signal.SL)
            {
                result.SetOutcome("SL", kline.OpenTime);
                return true;
            }
        }
        else
        {
            if (kline.LowPrice <= signal.TP1)
            {
                result.SetOutcome("TP1", kline.OpenTime);
                return true;
            }
            if (signal.TP2.HasValue && kline.LowPrice <= signal.TP2.Value)
            {
                result.SetOutcome("TP2", kline.OpenTime);
                return true;
            }
            if (kline.HighPrice >= signal.SL)
            {
                result.SetOutcome("SL", kline.OpenTime);
                return true;
            }
        }
        return false;
    }

    static void SaveToExcel(List<Signal> signals)
    {
        using (var package = new ExcelPackage(new FileInfo("Signals.xlsx")))
        {
            var ws = package.Workbook.Worksheets.Add("Señales");
            ws.Cells["A1"].LoadFromCollection(signals.Select(s => new {
                s.MessageID,
                s.UTC_DateTime,
                s.Symbol,
                s.Direction,
                s.Entry,
                s.TP1,
                TP2 = s.TP2?.ToString() ?? "N/A",
                s.SL
            }), true);
            package.Save();
        }
    }
}

public class Signal
{
    public int MessageID { get; set; }
    public DateTime UTC_DateTime { get; set; }
    public string Symbol { get; set; }
    public string Direction { get; set; }
    public decimal Entry { get; set; }
    public decimal TP1 { get; set; }
    public decimal? TP2 { get; set; }
    public decimal SL { get; set; }
}

public class BacktestResult
{
    public Signal Signal { get; set; }
    public string Outcome { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime OutcomeTime { get; set; }
    public decimal LowestAfterEntry { get; set; }
    public decimal HighestAfterEntry { get; set; }

    public void SetOutcome(string outcome, DateTime time)
    {
        Outcome = outcome;
        OutcomeTime = time;
    }
}