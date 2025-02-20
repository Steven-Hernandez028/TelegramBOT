using CryptoExchange.Net.Sockets;
using Newtonsoft.Json;
using OfficeOpenXml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBot;

internal class BinanceBackTest
{

    public class Backtester
    {
        private const string BinanceApiUrl = "https://fapi.binance.com/fapi/v1/klines";
        private const decimal InitialBalance = 100m;
        private const int Leverage = 50;
        private const decimal LiquidationPercentage = 0.04m; // 4% for 25x leverage

        public async Task RunBacktest()
        {
            var signals = ReadSignalsFromExcel();
            if (signals.Count == 0) return;

            var results = new List<BacktestResult>();

            foreach (var signal in signals)
            {
                var (startTime, endTime) = GetTimeRange(signal);

                var historicalData = await GetHistoricalData(signal.Symbol, startTime, endTime);
                if (historicalData == null || historicalData.Count == 0) continue;

                var result = SimulateTrade(signal, historicalData);
                results.Add(result);
            }

            PrintResults(results);
        }

        private List<TradeSignal> ReadSignalsFromExcel()
        {
            var signals = new List<TradeSignal>();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage(new System.IO.FileInfo("signals.xlsx")))
            {
                var worksheet = package.Workbook.Worksheets["Signals"];
                int rowCount = worksheet.Dimension.Rows;

                for (int row = 2; row <= rowCount; row++)
                {
                    var signal = new TradeSignal
                    {
                        Timestamp = DateTime.Parse(worksheet.Cells[row, 1].Text),
                        Symbol = worksheet.Cells[row, 2].Text,
                        Position = worksheet.Cells[row, 3].Text,
                        Entry = decimal.Parse(worksheet.Cells[row, 4].Text),
                        TakeProfits = new[]
                        {
                        GetDecimalOrZero(worksheet.Cells[row, 5]),
                        GetDecimalOrZero(worksheet.Cells[row, 6]),
                        GetDecimalOrZero(worksheet.Cells[row, 7]),
                        GetDecimalOrZero(worksheet.Cells[row, 8])
                    },
                        StopLoss = decimal.Parse(worksheet.Cells[row, 9].Text)
                    };

                    signals.Add(signal);
                }
            }
            return signals;
        }

        private decimal GetDecimalOrZero(ExcelRange cell) =>
            string.IsNullOrEmpty(cell.Text) ? 0 : decimal.Parse(cell.Text);

        private (DateTime start, DateTime end) GetTimeRange(TradeSignal signal)
        {
            DateTime max = DateTime.MinValue;

            if (signal.Timestamp > max) max = signal.Timestamp;

            max = max.AddDays(1) > DateTime.Today ? DateTime.Now : max.AddDays(1);
            return (signal.Timestamp, max);
        }

        private async Task<List<BinanceKline>?> GetHistoricalData(string symbol, DateTime start, DateTime end)
        {
            using (var client = new HttpClient())
            {
                var startTime = ((DateTimeOffset)start).ToUnixTimeMilliseconds();
                var endTime = ((DateTimeOffset)end).ToUnixTimeMilliseconds();

                try
                {
                    string query = $"{BinanceApiUrl}?symbol={symbol}&interval=15m&startTime={startTime}&endTime={endTime}";
                    var response = await client.GetStringAsync(query);


                    return JsonConvert.DeserializeObject<List<object[]>>(response)
                   .ConvertAll(k => new BinanceKline
                   {
                       OpenTime = (long)k[0],
                       OpenPrice = decimal.Parse(k[1].ToString()),
                       HighPrice = decimal.Parse(k[2].ToString()),
                       LowPrice = decimal.Parse(k[3].ToString()),
                       ClosePrice = decimal.Parse(k[4].ToString())
                   });
                }catch(Exception ex) { 
                    
                    Console.WriteLine(ex.ToString());   
                }


                return null;
            }
        }

        private BacktestResult SimulateTrade(TradeSignal signal, List<BinanceKline> data)
        {
            var result = new BacktestResult
            {
                Symbol = signal.Symbol,
                EntryTime = signal.Timestamp,
                Position = signal.Position,
                EntryPrice = signal.Entry
            };

            decimal liquidationPrice = signal.Position == "LONG"
                ? signal.Entry * (1 - LiquidationPercentage)
                : signal.Entry * (1 + LiquidationPercentage);

            foreach (var kline in data)
            {
                if (kline.OpenTime < ((DateTimeOffset)signal.Timestamp).ToUnixTimeMilliseconds())
                    continue;

                // Check liquidation
                if ((signal.Position == "LONG" && kline.LowPrice <= liquidationPrice) ||
                    (signal.Position == "SHORT" && kline.HighPrice >= liquidationPrice))
                {
                    result.Liquidated = true;
                    result.ExitPrice = liquidationPrice;
                    result.Pnl = -InitialBalance;
                    return result;
                }

                // Check Stop Loss
                if ((signal.Position == "LONG" && kline.LowPrice <= signal.StopLoss) ||
                    (signal.Position == "SHORT" && kline.HighPrice >= signal.StopLoss))
                {
                    result.HitStopLoss = true;
                    result.ExitPrice = signal.StopLoss;
                    result.Pnl = CalculatePnl(signal, signal.StopLoss);
                    return result;
                }

                // Check Take Profits
                for (int i = 0; i < signal.TakeProfits.Length; i++)
                {
                    decimal tp = signal.TakeProfits[i];
                    if (tp == 0) continue;

                    if ((signal.Position == "LONG" && kline.HighPrice >= tp) ||
                        (signal.Position == "SHORT" && kline.LowPrice <= tp))
                    {
                        result.TpHit = i + 1;
                        result.ExitPrice = tp;
                        result.Pnl = CalculatePnl(signal, tp);
                        return result;
                    }
                }
            }

            // If no exit condition met, close at last price
            result.ExitPrice = data[^1].ClosePrice;
            result.Pnl = CalculatePnl(signal, data[^1].ClosePrice);
            return result;
        }

        private decimal CalculatePnl(TradeSignal signal, decimal exitPrice)
        {
            decimal priceDifference = exitPrice - signal.Entry;
            decimal positionSize = InitialBalance * Leverage / signal.Entry;

            return signal.Position == "LONG"
                ? positionSize * priceDifference
                : positionSize * -priceDifference;
        }

        private void PrintResults(List<BacktestResult> results)
        {
            decimal totalPnl = 0;
            int liquidations = 0;
            int stopLossHits = 0;
            int tpHits = 0;

            foreach (var result in results)
            {
                totalPnl += result.Pnl;
                if (result.Liquidated) liquidations++;
                if (result.HitStopLoss) stopLossHits++;
                if (result.TpHit > 0) tpHits++;
            }

            Console.WriteLine("Backtesting Results:");
            Console.WriteLine($"Total Operations: {results.Count}");
            Console.WriteLine($"Total PNL: {totalPnl:F2} USD");
            Console.WriteLine($"Profit Factor: {results.Where(r => r.Pnl > 0).Sum(r => r.Pnl) / -results.Where(r => r.Pnl < 0).Sum(r => r.Pnl):F2}");
            Console.WriteLine($"Liquidations: {liquidations}");
            Console.WriteLine($"Stop Loss Hits: {stopLossHits}");
            Console.WriteLine($"Take Profit Hits: {tpHits}");
            Console.WriteLine($"Win Rate: {(decimal)tpHits / results.Count * 100:F2}%");
        }
    }
}


public class TradeSignal
{
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; }
    public string Position { get; set; }
    public decimal Entry { get; set; }
    public decimal[] TakeProfits { get; set; }
    public decimal StopLoss { get; set; }
}

public class BacktestResult
{
    public string Symbol { get; set; }
    public DateTime EntryTime { get; set; }
    public string Position { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Pnl { get; set; }
    public bool Liquidated { get; set; }
    public bool HitStopLoss { get; set; }
    public int TpHit { get; set; }
}

public class BinanceKline
{
    public long OpenTime { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
}
