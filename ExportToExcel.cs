using CryptoExchange.Net.SharedApis;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
namespace Excel;
public record TradingSignal(
    DateTime Timestamp,
    string Symbol,
    string Position,
    decimal EntryPrice,
    decimal? TP1,
    decimal? TP2,
    decimal? TP3,
    decimal? TP4,
    decimal SL
);

public class ExportExcel
{
  
    private List<string> _input;

    public ExportExcel(List<string> _input){
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        this._input= _input;
    }
    public void Run(){

         var signals = ParseSignals(_input);
         
    
        CreateExcel(signals, "signals.xlsx");
        Console.WriteLine("Archivo Excel creado exitosamente.");
    }
    /// <summary>
    /// Parsea el texto de entrada y devuelve una lista de señales de trading.
    /// </summary>
    private static List<TradingSignal> ParseSignals(List<string> lines)
    {
        var signals = new List<TradingSignal>();
        List<string> currentBlock = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("## "))
            {
                if (currentBlock != null)
                {
                    var signal = ParseSignalBlock(currentBlock);
                    if (signal != null)
                        signals.Add(signal);
                }
                currentBlock = new List<string> { trimmedLine };
            }
            else if (currentBlock != null)
            {
                currentBlock.Add(trimmedLine);
            }
        }

        if (currentBlock != null)
        {
            var signal = ParseSignalBlock(currentBlock);
            if (signal != null)
                signals.Add(signal);
        }

        return signals;
    }

    /// <summary>
    /// Parsea un bloque de líneas correspondiente a una señal de trading.
    /// </summary>
    private static TradingSignal ParseSignalBlock(List<string> block)
    {
        if (block.Count == 0)
            return null;

        // Primera línea: timestamp, símbolo y posición
        var firstLine = block[0];
        var timestampMatch = Regex.Match(firstLine, @"\d{1,2}/\d{1,2}/\d{4} \d{1,2}:\d{2}:\d{2} (?:AM|PM)");
        if (!timestampMatch.Success)
            return null;
 
        string timestampStr = timestampMatch.Groups[0].Value;
        DateTime timestamp = DateTime.ParseExact(timestampStr, "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture).AddHours(-4);

        var symbolPositionMatch = Regex.Match(firstLine, @"\ ([A-Z]+USDT\.P) (LONG|SHORT) / Cross 25x");
        if (!symbolPositionMatch.Success)
            return null;

        string symbol = symbolPositionMatch.Groups[1].Value.Substring(0, symbolPositionMatch.Groups[1].Value.IndexOf('.')) ;
        string position = symbolPositionMatch.Groups[2].Value;

        decimal? entryPrice = null;
        decimal? tp1 = null, tp2 = null, tp3 = null, tp4 = null;
        decimal? sl = null;

        foreach (var line in block)
        {
            if (line.Contains("• Entry: "))
            {
                var entryMatch = Regex.Match(line, @"• Entry: (\d+\.\d+)");
                if (entryMatch.Success)
                    entryPrice = decimal.Parse(entryMatch.Groups[1].Value);
            }
             if (line.Contains("• TP1:"))
            {
                var tpMatches = Regex.Matches(line, @"TP\d: (\d+\.\d+)");
                for (int i = 0; i < tpMatches.Count; i++)
                {
                    decimal tpValue = decimal.Parse(tpMatches[i].Groups[1].Value);

                    if (i == 0) tp1 = tpValue;
                    else if (i == 1) tp2 = tpValue;
                    else if (i == 2) tp3 = tpValue;
                    else if (i == 3) tp4 = tpValue;
                }
            }
             if (line.Contains("• SL:"))
            {
                var slMatch = Regex.Match(line, @"• SL: (\d+\.\d+)");
                if (slMatch.Success)
                    sl = position is "LONG" ? entryPrice - (entryPrice * .09m) : entryPrice + (entryPrice * .09m);  //VERSION TEST 0.2
                // decimal.Parse(slMatch.Groups[1].Value); VERSION ORIGINAL
                //    sl = position is "LONG" ? entryPrice - (entryPrice * .05m) : entryPrice - (entryPrice * .05m); VERSION TEST 0.1
            }
        }

        // Verificar que se hayan encontrado los campos obligatorios
        if (entryPrice.HasValue && sl.HasValue)
        {
            return new TradingSignal(timestamp, symbol, position, entryPrice.Value, tp1, tp2, tp3, tp4, sl.Value);
        }
        return null;
    }

    /// <summary>
    /// Crea un archivo Excel con las señales de trading organizadas en columnas.
    /// </summary>
    private static void CreateExcel(List<TradingSignal> signals, string filePath)
    {
        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Signals");

            // Establecer encabezados de columna
            worksheet.Cells[1, 1].Value = "Signal Timestamp";
            worksheet.Cells[1, 2].Value = "Symbol";
            worksheet.Cells[1, 3].Value = "Position";
            worksheet.Cells[1, 4].Value = "Entry Price";
            worksheet.Cells[1, 5].Value = "TP1";
            worksheet.Cells[1, 6].Value = "TP2";
            worksheet.Cells[1, 7].Value = "TP3";
            worksheet.Cells[1, 8].Value = "TP4";
            worksheet.Cells[1, 9].Value = "SL";

            // Escribir datos
            for (int i = 0; i < signals.Count; i++)
            {
                var signal = signals[i];
                int row = i + 2;
                worksheet.Cells[row, 1].Value = signal.Timestamp;
                worksheet.Cells[row, 1].Style.Numberformat.Format = "MM/dd/yyyy HH:mm:ss";
                worksheet.Cells[row, 2].Value = signal.Symbol;
                worksheet.Cells[row, 3].Value = signal.Position;
                worksheet.Cells[row, 4].Value = signal.EntryPrice;
                if (signal.TP1.HasValue) worksheet.Cells[row, 5].Value = signal.TP1.Value;
                if (signal.TP2.HasValue) worksheet.Cells[row, 6].Value = signal.TP2.Value;
                if (signal.TP3.HasValue) worksheet.Cells[row, 7].Value = signal.TP3.Value;
                if (signal.TP4.HasValue) worksheet.Cells[row, 8].Value = signal.TP4.Value;
                worksheet.Cells[row, 9].Value = signal.SL;
            }

            // Guardar el archivo
            package.SaveAs(new FileInfo(filePath));
        }
    }
}