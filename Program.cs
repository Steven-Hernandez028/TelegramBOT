using TelegramBot;
using Excel;
using static TelegramBot.BinanceBackTest;

TelegramReader reader = new TelegramReader();
await reader.Run();
var result = await reader.ReadGroupMessages();
ExportExcel excel = new ExportExcel(result);
excel.Run();

Backtester backtester = new Backtester();
await backtester.RunBacktest();


