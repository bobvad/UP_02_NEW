using Newtonsoft.Json;
using System.Text;
using API_UP_02.GigaChat_LLM.Model_GigaChat;
using API_UP_02.GigaChat_LLM.For_GigaChat.Models;

namespace API_UP_02.Services
{
    public class GigaChatService
    {
        private static string ClientId = "0199d470-bb93-7ce2-b0df-620ead27395d";
        private static string AuthorizationKey = "MDE5OWQ0NzAtYmI5My03Y2UyLWIwZGYtNjIwZWFkMjczOTVkOjQwNjdkNDdhLWY1MTYtNGZiYS05ZGM5LTg0MDAwNDExNTUwNQ==";

        private static string Token = null;
        private static DateTime TokenExpirationTime;

        private const string SystemPrompt = @"Ты - книжный рекомендательный сервис, специализирующийся на книгах с сайта Lитмир (https://www.litmir.me/). 
              Твоя задача - рекомендовать книги пользователям на основе их запросов.
              Для каждой рекомендации указывай:
              1. Название книги
              2. Автора
              3. Краткое описание (2-3 предложения)
              4. Ссылку на книгу на Lитмир (если знаешь точную ссылку, иначе предлагай поиск на сайте)
              5. Почему эта книга подходит под запрос пользователя

              Старайся давать 2-3 рекомендации на каждый запрос. Будь дружелюбным и полезным.";

        public async Task<string> GetBookRecommendation(string userRequest, List<Request.Message> conversationHistory = null)
        {
            try
            {
                await EnsureTokenAsync();

                if (conversationHistory == null)
                    conversationHistory = new List<Request.Message>();

                if (conversationHistory.Count == 0)
                {
                    conversationHistory.Add(new Request.Message()
                    {
                        role = "system",
                        content = SystemPrompt
                    });
                }

                conversationHistory.Add(new Request.Message()
                {
                    role = "user",
                    content = userRequest
                });

                var response = await GetAnswer(Token, conversationHistory);

                if (response?.choices != null && response.choices.Count > 0)
                {
                    string assistantResponse = response.choices[0].message.content;

                    conversationHistory.Add(new Request.Message()
                    {
                        role = "assistant",
                        content = assistantResponse
                    });

                    return assistantResponse;
                }

                return "Извините, не удалось получить рекомендации. Попробуйте переформулировать запрос.";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        public async Task<string> GetToken()
        {
            string rqUID = Guid.NewGuid().ToString();
            string ReturnToken = null;
            string Url = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";

            using (HttpClientHandler Handler = new HttpClientHandler())
            {
                Handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyError) => true;

                using (HttpClient client = new HttpClient(Handler))
                {
                    HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, Url);
                    Request.Headers.Add("Accept", "application/json");
                    Request.Headers.Add("RqUID", rqUID);
                    Request.Headers.Add("Authorization", $"Basic {AuthorizationKey}");

                    var Data = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("scope", "GIGACHAT_API_PERS")
                    };

                    Request.Content = new FormUrlEncodedContent(Data);
                    HttpResponseMessage Response = await client.SendAsync(Request);

                    if (Response.IsSuccessStatusCode)
                    {
                        string ResponseContent = await Response.Content.ReadAsStringAsync();
                        ResponseToken Token = JsonConvert.DeserializeObject<ResponseToken>(ResponseContent);
                        ReturnToken = Token.access_token;
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка при получении токена: {Response.StatusCode}");
                        Console.WriteLine(await Response.Content.ReadAsStringAsync());
                    }
                }
            }
            return ReturnToken;
        }

        private async Task EnsureTokenAsync()
        {
            if (Token == null || TokenExpirationTime <= DateTime.UtcNow)
            {
                Token = await GetToken();
                TokenExpirationTime = DateTime.UtcNow.AddMinutes(30); 
            }
        }

        public async Task<ResponseMessage> GetAnswer(string token, List<Request.Message> messages)
        {
            ResponseMessage responseMessage = null;
            string Url = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";

            using (HttpClientHandler Handler = new HttpClientHandler())
            {
                Handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true;

                using (HttpClient client = new HttpClient(Handler))
                {
                    HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, Url);
                    Request.Headers.Add("Accept", "application/json");
                    Request.Headers.Add("Authorization", $"Bearer {token}");
                    Request.Headers.Add("X-Client-ID", ClientId);

                    GigaChat_LLM.For_GigaChat.Models.Request DataRequest = new GigaChat_LLM.For_GigaChat.Models.Request()
                    {
                        model = "GigaChat",
                        stream = false,
                        repetition_penalty = 1,
                        messages = messages
                    };

                    string JsonContent = JsonConvert.SerializeObject(DataRequest);
                    Request.Content = new StringContent(JsonContent, Encoding.UTF8, "application/json");

                    HttpResponseMessage Response = await client.SendAsync(Request);

                    if (Response.IsSuccessStatusCode)
                    {
                        string ResponseContent = await Response.Content.ReadAsStringAsync();
                        responseMessage = JsonConvert.DeserializeObject<ResponseMessage>(ResponseContent);
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка API: {Response.StatusCode}");
                        Console.WriteLine(await Response.Content.ReadAsStringAsync());
                    }
                }
            }
            return responseMessage;
        }
    }
}