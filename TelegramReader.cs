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
        private static  string  _phoneNumber = "+18095860390"; 
        private static string _apiHash = "e31ef1eb8aa2fa045a6d4b6db52c6352"; 
        private static int _apiId = 29051232 ;
        internal const int WHALE_CRYPTO_KEY = 1951214339;


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

            int pages = Configuration.totalMessages / Configuration.limit;
            List<string> BlockMessage = new List<string>();
            int offset_id = 0;
            int add_offset = 0;
            for (int i = 0; i < pages; i++)
            {
                Messages_MessagesBase messages;

                if(Configuration.IsFilterDateFilter)
                {
                    messages = await _client.Messages_GetHistory(targetChat,
                                      offset_id: offset_id,
                                      add_offset: add_offset,
                                      offset_date: Configuration.TO_DATE,
                                      limit: Configuration.limit);

                }
                else
                {

                messages  = await _client.Messages_GetHistory(targetChat,
                    offset_id: offset_id,
                    add_offset: add_offset,
                    limit: Configuration.limit);
                }

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
                        if (Configuration.PAIR_BLACKLIST.Contains(pairsubstring) )
                            continue;
                        }
                        if (result.Success)
                        {
                            if (Configuration.IsFilterDateFilter && msg.date >= Configuration.FROM_DATE && msg.date <= Configuration.TO_DATE)
                            {
                                BlockMessage.Add($"## {msg.date} {msg.message.Replace("\n", "").Replace("\b", "").Replace("\t", "")} ##");
                            }

                            if (!Configuration.IsFilterDateFilter)
                            {
                                BlockMessage.Add($"## {msg.date} {msg.message.Replace("\n", "").Replace("\b", "").Replace("\t", "")} ##");
                            }
                        }
                    }
                }

                
                var lastMessage = messages.Messages.LastOrDefault() as Message;
                if (lastMessage != null)
                {
                    offset_id = lastMessage.id;
                    add_offset = 0; 
                }
                else
                {
                    break;
                }

                await Task.Delay(500); 
            }
            return BlockMessage;
        }
    }
}