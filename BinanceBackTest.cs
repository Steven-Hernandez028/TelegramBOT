using CryptoExchange.Net.Sockets;
using Newtonsoft.Json;
using OfficeOpenXml;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBot;

public class Backtester
{


    private string _path;
    private readonly HttpClient _httpClient = new HttpClient();

    public Backtester(string path)
    {
        _path = path;
    }

    public async Task RunBacktest()
    {
        var signals = ReadSignalsFromExcel();
        if (signals.Count == 0) return;

        var signalGroups = signals
            .GroupBy(s => s.Symbol)
            .Select(g => new
            {
                Symbol = g.Key,
                Signals = g.ToList(),
                StartTime = g.Min(s => s.Timestamp),
                EndTime = g.Max(s => GetEndTime(s))
            })
            .ToList();

        var historicalData = new ConcurrentDictionary<string, List<BinanceKline>>();
        var semaphore = new SemaphoreSlim(5, 5); // Limitar a 5 hilos simultáneos
        var fetchTasks = new List<Task>();

        foreach (var group in signalGroups)
        {
            await semaphore.WaitAsync();
            fetchTasks.Add(Task.Run(async () =>
            {
                try
                {

                    var data = await GetHistoricalData(group.Symbol, group.StartTime, group.EndTime);
                    historicalData.TryAdd(group.Symbol, data ?? new List<BinanceKline>());
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(fetchTasks);

        var results = new List<BacktestResult>();
        decimal currentBalance = Configuration.InitialBalance;

        foreach (var signal in signals)
        {
            if (!historicalData.TryGetValue(signal.Symbol, out var data) || data == null)
            {
                Console.WriteLine($"Datos no encontrados para {signal.Symbol}");
                continue;
            }

            var result = SimulateTrade(signal, data, currentBalance);
            results.Add(result);
            currentBalance += result.Pnl;
        }

        PrintResults(results, currentBalance);
    }

    private List<TradeSignal> ReadSignalsFromExcel()
    {
        var signals = new List<TradeSignal>();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using (var package = new ExcelPackage(new System.IO.FileInfo(_path)))
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
        DateTime max = signal.Timestamp.AddDays(1) > DateTime.UtcNow
           ? DateTime.UtcNow
           : signal.Timestamp.AddDays(1);

        return (
            signal.Timestamp.ToUniversalTime(),
            max.ToUniversalTime()
        );
    }

    private DateTime GetEndTime(TradeSignal signal)
    {
        var end = signal.Timestamp.AddDays(1);
        return end > DateTime.UtcNow ? DateTime.UtcNow : end;
    }

    private async Task<List<BinanceKline>> GetHistoricalData(string symbol, DateTime start, DateTime end)
    {
        var allKlines = new List<BinanceKline>();
        long startTime = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        long endTime = new DateTimeOffset(end).ToUnixTimeMilliseconds();

        try
        {
            while (true)
            {
                var url = $"{Configuration.BinanceApiUrl}?symbol={symbol}&interval={Configuration.Interval}&startTime={startTime}&endTime={endTime}&limit=1000";
                var response = await _httpClient.GetStringAsync(url);

                var klines = JsonConvert.DeserializeObject<List<object[]>>(response)
                    .Select(k => new BinanceKline
                    {
                        OpenTime = (long)k[0],
                        OpenPrice = decimal.Parse(k[1].ToString()),
                        HighPrice = decimal.Parse(k[2].ToString()),
                        LowPrice = decimal.Parse(k[3].ToString()),
                        ClosePrice = decimal.Parse(k[4].ToString()),
                        CloseTime = (long)k[6]
                    }).ToList();

                if (!klines.Any()) break;

                allKlines.AddRange(klines);

                if (klines.Last().OpenTime >= endTime) break;

                startTime = klines.Last().OpenTime + 1;

                await Task.Delay(100);
            }
            return allKlines;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en {symbol}: {ex.Message}");
            return allKlines;
        }
    }

    private BacktestResult SimulateTrade(TradeSignal signal, List<BinanceKline> data, decimal currentBalance)
    {
        decimal riskedAmount = currentBalance * Configuration.RiskPercentage;
        var result = new BacktestResult
        {
            Symbol = signal.Symbol,
            EntryTime = signal.Timestamp,
            Position = signal.Position,
            TakeProfit1 = signal.TakeProfits.ElementAtOrDefault(0),
            TakeProfit2 = signal.TakeProfits.Length > 1 ? signal.TakeProfits[1] : (decimal?)null,
            TakeProfit3 = signal.TakeProfits.Length > 2 ? signal.TakeProfits[2] : (decimal?)null,
            stopLoss = signal.StopLoss,
            EntryPrice = signal.Entry
        };

        decimal liquidationPrice = signal.Position == "LONG"
            ? Helper.CalculateLongLiquidationPrice(signal.Entry, riskedAmount, Configuration.Leverage)
            : Helper.CalculateShortLiquidationPrice(signal.Entry, riskedAmount, Configuration.Leverage);
        int id = 0;
        foreach (var kline in data)
        {
            id++;
            var TimeOffset = new DateTimeOffset(signal.Timestamp).ToUnixTimeMilliseconds();

            //WARNING THIS MIGHT BLOW YOUR BUFFER IN DEBUG:
            // Console.WriteLine($"{DateTimeOffset.FromUnixTimeMilliseconds(kline.OpenTime).UtcDateTime:yyyy-MM-dd HH:mm:ss} - {DateTimeOffset.FromUnixTimeMilliseconds(TimeOffset).UtcDateTime:yyyy-MM-dd HH:mm:ss}");           
            if (kline.OpenTime < TimeOffset) continue;

            if ((signal.Position == "LONG" && kline.LowPrice <= liquidationPrice) ||
                 (signal.Position == "SHORT" && kline.HighPrice >= liquidationPrice))
            {
                result.Liquidated = true;
                result.ExitPrice = liquidationPrice;
                var (pnl, commission) = CalculatePnl(signal, liquidationPrice, riskedAmount);
                result.Pnl = pnl;
                result.CommissionPaid = commission;
                return result;
            }

            if ((signal.Position == "LONG" && kline.LowPrice <= signal.StopLoss) ||
                (signal.Position == "SHORT" && kline.HighPrice >= signal.StopLoss))
            {
                result.HitStopLoss = true;
                result.ExitPrice = signal.StopLoss;
                result.ExitTime = DateTimeOffset.FromUnixTimeMilliseconds(kline.CloseTime).UtcDateTime;
                var (pnl, commission) = CalculatePnl(signal, signal.StopLoss, riskedAmount);
                result.Pnl = pnl;
                result.CommissionPaid = commission;
                return result;
            }
            for (int i = 0; i < signal.TakeProfits.Length; i++)
            {
                decimal tp = signal.TakeProfits[i];
               // tp += tp * 0.013m;
                if (tp == 0) continue;

                if ((signal.Position == "LONG" && kline.HighPrice >= tp) ||
                    (signal.Position == "SHORT" && kline.LowPrice <= tp))
                {
                    result.ExitTime = DateTimeOffset.FromUnixTimeMilliseconds(kline.CloseTime).UtcDateTime;
                    result.TpHit = i + 1;

                    var (pnl, commission) = CalculatePnl(signal, tp, riskedAmount);
                    result.Pnl = pnl;
                    result.CommissionPaid = commission;
                    return result;
                }
            }

        }
        if (data.Count > 0)
        {
            result.ExitTime = DateTimeOffset.FromUnixTimeMilliseconds(data[^1].CloseTime).UtcDateTime;
            result.ExitPrice = data[^1].ClosePrice;
            var (pnl, commission) = CalculatePnl(signal, data[^1].ClosePrice, riskedAmount);

            result.Pnl = pnl;
            result.CommissionPaid = commission;

        }
        return result;
    }

    // 2. Modificar CalculatePnl para calcular comisiones
    private (decimal netPnl, decimal commission) CalculatePnl(TradeSignal signal, decimal exitPrice, decimal riskedAmount)
    {
        decimal priceDifference = exitPrice - signal.Entry;
        decimal positionSize = (riskedAmount * Configuration.Leverage) / signal.Entry;

        // PnL bruto
        decimal rawPnl = signal.Position == "LONG"
            ? positionSize * priceDifference
            : -positionSize * priceDifference;

        // Cálculo de comisiones
        decimal entryCommission = positionSize * signal.Entry * Configuration.CommissionRate;
        decimal exitCommission = positionSize * exitPrice * Configuration.CommissionRate;
        decimal totalCommission = entryCommission + exitCommission;

        return (rawPnl - totalCommission, totalCommission);
    }

    private void PrintResults(List<BacktestResult> results, decimal finalBalance)
    {
        decimal totalPnl = 0;
        int liquidations = 0;
        int stopLossHits = 0;
        int tpHits = 0;
        ExportResultsToExcel(results);
        foreach (var result in results)
        {
            totalPnl += result.Pnl;
            if (result.Liquidated) liquidations++;
            if (result.HitStopLoss) stopLossHits++;
            if (result.TpHit > 0) tpHits++;
        }
        decimal totalCommission = results.Sum(r => r.CommissionPaid);

        Console.WriteLine($"Total Comisiones: {totalCommission:F2} USD");
        Console.WriteLine("Backtesting Results:");
        Console.WriteLine($"Total Operations: {results.Count}");
        Console.WriteLine($"Total PNL: {totalPnl:F2} USD");
        Console.WriteLine($"Profit Factor: {results.Where(r => r.Pnl > 0).Sum(r => r.Pnl) / -results.Where(r => r.Pnl < 0).Sum(r => r.Pnl):F2}");
        Console.WriteLine($"Liquidations: {liquidations}");
        Console.WriteLine($"Stop Loss Hits: {stopLossHits}");
        Console.WriteLine($"Take Profit Hits: {tpHits}");
        Console.WriteLine($"Win Rate: {(decimal)tpHits / results.Count * 100:F2}%");
        Console.WriteLine($"Balance Final: {finalBalance:F2} USD");
    }
    private void ExportResultsToExcel(List<BacktestResult> results)
    {
        const string directory = "Results";
        if (!Directory.Exists("Results"))
            Directory.CreateDirectory(directory);

        var fileName = $"BacktestResults_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(directory, fileName);

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Results");

            // Encabezados
            worksheet.Cells[1, 1].Value = "Símbolo";
            worksheet.Cells[1, 2].Value = "Entrada";
            worksheet.Cells[1, 3].Value = "Salida";
            worksheet.Cells[1, 4].Value = "Dirección";
            worksheet.Cells[1, 5].Value = "Precio Entrada";
            worksheet.Cells[1, 6].Value = "Precio Salida";
            worksheet.Cells[1, 7].Value = "Beneficio";
            worksheet.Cells[1, 8].Value = "TP1";
            worksheet.Cells[1, 9].Value = "TP2";
            worksheet.Cells[1, 10].Value = "TP3";
            worksheet.Cells[1, 11].Value = "Stop Loss";
            worksheet.Cells[1, 12].Value = "Liquidado";
            worksheet.Cells[1, 13].Value = "Hit SL";
            worksheet.Cells[1, 14].Value = "TP Alcanzado";
            worksheet.Cells[1, 15].Value = "Comisiones";
            for (int i = 0; i < results.Count; i++)
            {
                var row = i + 2;
                var r = results[i];

                worksheet.Cells[row, 1].Value = r.Symbol;
                worksheet.Cells[row, 2].Value = r.EntryTime.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cells[row, 3].Value = r.ExitTime.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cells[row, 4].Value = r.Position;
                worksheet.Cells[row, 5].Value = r.EntryPrice;
                worksheet.Cells[row, 6].Value = r.ExitPrice;
                worksheet.Cells[row, 7].Value = r.Pnl;
                worksheet.Cells[row, 8].Value = r.TakeProfit1;
                worksheet.Cells[row, 9].Value = r.TakeProfit2 ?? 0;
                worksheet.Cells[row, 10].Value = r.TakeProfit3 ?? 0;
                worksheet.Cells[row, 11].Value = r.stopLoss;
                worksheet.Cells[row, 12].Value = r.Liquidated ? "Sí" : "No";
                worksheet.Cells[row, 13].Value = r.HitStopLoss ? "Sí" : "No";
                worksheet.Cells[row, 14].Value = r.TpHit > 0 ? $"TP{r.TpHit}" : "No";
                worksheet.Cells[row, 15].Value = results[i].CommissionPaid;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));
        }

        Console.WriteLine($"\nResultados exportados a: {filePath}");
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
    public decimal CommissionPaid { get; set; } // Opcional para reporte

    public DateTime EntryTime { get; set; }
    public string Position { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal Pnl { get; set; }
    public bool Liquidated { get; set; }
    public bool HitStopLoss { get; set; }
    public int TpHit { get; set; }
    public decimal TakeProfit1 { get; set; }
    public decimal? TakeProfit2 { get; set; }
    public decimal? TakeProfit3 { get; set; }
    public decimal? stopLoss { get; set; }
}

public class BinanceKline
{
    public long OpenTime { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public long CloseTime { get; set; }

}
