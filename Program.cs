using TelegramBot;
using Excel;



TelegramReader reader = new TelegramReader();
await reader.Run();
var result = await reader.ReadGroupMessages();
ExportExcel excel = new ExportExcel(result);
excel.Run();
var path = excel.GetFilePath();


Backtester backtester = new Backtester(path);
await backtester.RunBacktest();


