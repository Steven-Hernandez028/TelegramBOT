using System;
using System.Text;
using System.Text.RegularExpressions;
using TL;
using WTelegram;

namespace TelegramBot
{
    public class TelegramReader
    {
        private static Client? _client;
        private static  string  _phoneNumber = "+18095860390"; // Your phone number with country code
        private static string _apiHash = "e31ef1eb8aa2fa045a6d4b6db52c6352"; // From my.telegram.org
        private static int _apiId = 29051232 ; // From my.telegram.org
        private  const int WHALE_CRYPTO_KEY = 1951214339;

        private readonly string[] PAIR_BLACKLIST = ["LITUSDT", "ORBUSDT", "MEMEUSDT", "SLPUSDT", "IOSTUSDT", "BATUSDT"];
        


        public TelegramReader(){
                  _client = new Client(_apiId, _apiHash);

        }
        public  async Task Run()
        {
                  
            try
            {
                await DoLogin();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }


        static async Task DoLogin()
        {
            while (_client!.User == null)
            {
                string what = await _client.Login(_phoneNumber);
                switch (what)
                {
                    case "verification_code":
                        Console.Write("Enter the verification code: ");
                        string? code = Console.ReadLine();
                        await _client.Login(code);
                        break;
                    case "password":
                        Console.Write("Enter your password: ");
                        string?  password = Console.ReadLine();
                        await _client.Login(password);
                        break;
                }
            }
            Console.WriteLine($"Logged in as {_client.User}");
        }

       public  async Task<    List<string>?> ReadGroupMessages()
        {
            // Get all chats
            var chats = await _client!.Messages_GetAllChats();
            
            ChatBase targetChat = null!;
            foreach (var chat in chats.chats)
            {

                if (chat.Key is WHALE_CRYPTO_KEY)
                {
                    targetChat = chat.Value;
                    break;
                }
            }

            if (targetChat == null)
            {
                Console.WriteLine("Group not found!");
                return null;
            }
            int totalMessages = 800;
            int limit = 100;
            int pages = totalMessages / limit;
            List<string> BlockMessage = new List<string>();
            int offset_id = 0;
            int add_offset = 0;

            for (int i = 0; i < pages; i++)
            {
                var messages = await _client.Messages_GetHistory(targetChat,
                    offset_id: offset_id,
                    add_offset: add_offset,
                    limit: limit);

                if (messages.Messages.Count() == 0) break;

                foreach (var msgBase in messages.Messages)
                {
                    if (msgBase is Message msg)
                    {
              

                        Regex regex = new Regex(@"^(?!.*Leverage)([A-Z]+USDT\.P)\s+(LONG|SHORT)", RegexOptions.Multiline);
                        var result = regex.Match(msg.message);
                        if(result.Value.IndexOf(".") > 0)
                        {

                        var pairsubstring = result.Value.Substring(0, result.Value.IndexOf(".") );
                        if (PAIR_BLACKLIST.Contains(pairsubstring) )
                            continue;
                        }
                        if (result.Success)
                        {
                            BlockMessage.Add($"## {msg.date} {msg.message.Replace("\n", "").Replace("\b", "").Replace("\t", "")} ##");
                        }
                    }
                }

                // Actualizar el offset_id con el último mensaje recibido
                var lastMessage = messages.Messages.LastOrDefault() as Message;
                if (lastMessage != null)
                {
                    offset_id = lastMessage.id;
                    add_offset = 0; // Cargar mensajes más antiguos que el último ID
                }
                else
                {
                    break;
                }

                await Task.Delay(500); // Delay para evitar límites de rate limit
            }
            return BlockMessage;
        }
    }
}