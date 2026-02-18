using API_UP_02.Context;
using API_UP_02.GigaChat_LLM.For_GigaChat.Models;
using API_UP_02.GigaChat_LLM.Model_GigaChat;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;

namespace API_UP_02.Services
{
    public class GigaChatService
    {
        private static string ClientId = "0199d470-bb93-7ce2-b0df-620ead27395d";
        private static string AuthorizationKey = "MDE5OWQ0NzAtYmI5My03Y2UyLWIwZGYtNjIwZWFkMjczOTVkOjQwNjdkNDdhLWY1MTYtNGZiYS05ZGM5LTg0MDAwNDExNTUwNQ==";
        private static string Token = null;
        private static DateTime TokenExpirationTime;

        private readonly BooksContext _context;
        private readonly ILogger<GigaChatService> _logger;

        private const string SystemPrompt = @"Ты - книжный рекомендательный сервис.

Твоя задача - рекомендовать книги пользователям на основе их запросов и предпочтений.

ВАЖНО: Все рекомендации должны быть реальными книгами

Для каждой рекомендации обязательно указывай:
1. Название книги
2. Автора
3. Краткое описание (2-3 предложения)
4. Почему эта книга подходит под запрос пользователя

Формат ответа:
По вашему запросу я рекомендую:

[Название книги] - [Автор]
[Описание]
[Почему подходит]

Старайся давать 2-3 рекомендации на каждый запрос.";

        public GigaChatService(BooksContext context, ILogger<GigaChatService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Получение рекомендаций по поисковому запросу
        /// </summary>
        public async Task<string> GetBookRecommendation(string userRequest, List<Request.Message> conversationHistory = null)
        {
            try
            {
                _logger.LogInformation($"Получен запрос: {userRequest}");

                await EnsureTokenAsync();

                if (conversationHistory == null)
                    conversationHistory = new List<Request.Message>();

                if (conversationHistory.Count == 0 || !conversationHistory.Any(m => m.role == "system"))
                {
                    conversationHistory.Insert(0, new Request.Message()
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
                _logger.LogError(ex, "Ошибка при получении рекомендации");
                return $"Произошла ошибка: {ex.Message}. Пожалуйста, попробуйте позже.";
            }
        }

        /// <summary>
        /// Получение персональных рекомендаций на основе истории чтения пользователя
        /// </summary>
        public async Task<string> GetPersonalizedRecommendation(int userId)
        {
            try
            {
                _logger.LogInformation($"Персональная рекомендация для пользователя {userId}");

                await EnsureTokenAsync();

                var prompt = await BuildPersonalizedPrompt(userId);

                var messages = new List<Request.Message>
                {
                    new Request.Message { role = "system", content = SystemPrompt },
                    new Request.Message { role = "user", content = prompt }
                };

                var response = await GetAnswer(Token, messages);

                return response?.choices?.FirstOrDefault()?.message?.content
                       ?? "Не удалось подобрать персональные рекомендации";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка персональной рекомендации для {userId}");
                return "Что-то пошло не так. Попробуйте позже.";
            }
        }

        /// <summary>
        /// Получение и обновление токена GigaChat
        /// </summary>
        public async Task<string> GetToken()
        {
            string rqUID = Guid.NewGuid().ToString();
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
                        var token = JsonConvert.DeserializeObject<ResponseToken>(ResponseContent);
                        _logger.LogInformation("Токен успешно получен");
                        return token?.access_token;
                    }
                    else
                    {
                        string error = await Response.Content.ReadAsStringAsync();
                        _logger.LogError($"Ошибка получения токена: {Response.StatusCode} - {error}");
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Отправка запроса к GigaChat API
        /// </summary>
        public async Task<ResponseMessage> GetAnswer(string token, List<Request.Message> messages)
        {
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

                    var DataRequest = new Request()
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
                        var responseMessage = JsonConvert.DeserializeObject<ResponseMessage>(ResponseContent);

                        if (responseMessage?.usage != null)
                        {
                            _logger.LogInformation($"Использовано токенов: {responseMessage.usage.total_tokens}");
                        }

                        return responseMessage;
                    }
                    else
                    {
                        string error = await Response.Content.ReadAsStringAsync();
                        _logger.LogError($"Ошибка API GigaChat: {Response.StatusCode} - {error}");
                        return null;
                    }
                }
            }
        }

        #region Вспомогательные методы

        private async Task EnsureTokenAsync()
        {
            if (Token == null || TokenExpirationTime <= DateTime.UtcNow)
            {
                Token = await GetToken();
                TokenExpirationTime = DateTime.UtcNow.AddMinutes(30);
                _logger.LogInformation("Токен обновлен");
            }
        }

        private async Task<string> BuildPersonalizedPrompt(int userId)
        {
            var readingProgress = await _context.ReadingProgress
                .Include(rp => rp.Book)
                .Where(rp => rp.UserId == userId)
                .ToListAsync();

            var favoriteBooks = await _context.Favorites
                .Include(f => f.Book)
                .Where(f => f.UserId == userId)
                .Select(f => f.Book)
                .ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("Порекомендуй мне книги на основе моей библиотеки:");
            sb.AppendLine();

            var finishedBooks = readingProgress
                .Where(rp => rp.Status == "Прочитано")
                .Select(rp => rp.Book)
                .ToList();

            if (finishedBooks.Any())
            {
                sb.AppendLine("Книги, которые я уже прочитал:");
                foreach (var book in finishedBooks.Take(5))
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author} ({book.Genre})");
                }
            }

            var currentlyReading = readingProgress
                .Where(rp => rp.Status == "Читаю")
                .Select(rp => rp.Book)
                .ToList();

            if (currentlyReading.Any())
            {
                sb.AppendLine("\nСейчас читаю:");
                foreach (var book in currentlyReading)
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author}");
                }
            }

            var wantToRead = readingProgress
                .Where(rp => rp.Status == "Хочу прочитать")
                .Select(rp => rp.Book)
                .ToList();

            if (wantToRead.Any())
            {
                sb.AppendLine("\nХочу прочитать:");
                foreach (var book in wantToRead.Take(3))
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author}");
                }
            }

            if (favoriteBooks.Any())
            {
                sb.AppendLine("\nМои любимые книги:");
                foreach (var book in favoriteBooks.Take(3))
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author}");
                }
            }

            sb.AppendLine("\nПосоветуй 3 книги, которые мне должны понравиться, с учетом моих предпочтений.");
            sb.AppendLine("Напиши названия, авторов и почему они мне понравятся.");

            return sb.ToString();
        }
        #endregion
    }
}