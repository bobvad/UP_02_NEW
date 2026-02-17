// Services/GigaChatService.cs
using API_UP_02.Models;
using API_UP_02.GigaChat_LLM.For_GigaChat.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;
using API_UP_02.GigaChat_LLM.Model_GigaChat;
using API_UP_02.Context;

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

        private const string SystemPrompt = @"Ты - книжный рекомендательный сервис, специализирующийся на книгах . 

Твоя задача - рекомендовать книги пользователям на основе их запросов и предпочтений.

ВАЖНО: Все рекомендации должны быть реальными книгами

Для каждой рекомендации обязательно указывай:
1. Название книги
2. Автора
3. Краткое описание (2-3 предложения)
5. Почему эта книга подходит под запрос пользователя

Формат ответа:
 По вашему запросу я рекомендую:

 [Название книги] - [Автор]
 [Описание]
 [Ссылка]
 [Почему подходит]

Старайся давать 2-3 рекомендации на каждый запрос. Будь дружелюбным и используй эмодзи.";

        public GigaChatService(BooksContext context, ILogger<GigaChatService> logger)
        {
            _context = context;
            _logger = logger;
        }

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
        public async Task<string> GetPersonalizedRecommendation(int userId)
        {
            try
            {
                _logger.LogInformation($"Персональная рекомендация для пользователя {userId}");

                await EnsureTokenAsync();

                var userContext = await GetUserReadingContext(userId);

                var prompt = BuildPersonalizedPrompt(userContext);

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

        private async Task<UserReadingContext> GetUserReadingContext(int userId)
        {
            var context = new UserReadingContext();

            var readingProgress = await _context.ReadingProgress
                .Include(rp => rp.Book)
                .Where(rp => rp.UserId == userId)
                .ToListAsync();

            var favorites = await _context.Favorites
                .Include(f => f.Book)
                .Where(f => f.UserId == userId)
                .Select(f => f.Book)
                .ToListAsync();

            foreach (var progress in readingProgress)
            {
                var bookInfo = new BookInfo
                {
                    Title = progress.Book.Title,
                    Author = progress.Book.Author,
                    Genre = progress.Book.Genre
                };

                switch (progress.Status)
                {
                    case "Хочу прочитать":
                        context.WantToRead.Add(bookInfo);
                        break;
                    case "Читаю":
                        context.CurrentlyReading.Add(bookInfo);
                        break;
                    case "Прочитано":
                        context.FinishedBooks.Add(bookInfo);
                        break;
                }
            }

            foreach (var fav in favorites)
            {
                context.FavoriteBooks.Add(new BookInfo
                {
                    Title = fav.Title,
                    Author = fav.Author,
                    Genre = fav.Genre
                });
            }

            return context;
        }

        private string BuildPersonalizedPrompt(UserReadingContext context)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Порекомендуй мне книги на основе моей библиотеки:");
            sb.AppendLine();

            if (context.FinishedBooks.Any())
            {
                sb.AppendLine("📚 Книги, которые я уже прочитал:");
                foreach (var book in context.FinishedBooks.Take(5))
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author} ({book.Genre})");
                }
            }

            if (context.CurrentlyReading.Any())
            {
                sb.AppendLine("\n📖 Сейчас читаю:");
                foreach (var book in context.CurrentlyReading)
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author}");
                }
            }

            if (context.WantToRead.Any())
            {
                sb.AppendLine("\n⏳ Хочу прочитать:");
                foreach (var book in context.WantToRead.Take(3))
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author}");
                }
            }

            if (context.FavoriteBooks.Any())
            {
                sb.AppendLine("\n❤️ Мои любимые книги:");
                foreach (var book in context.FavoriteBooks.Take(3))
                {
                    sb.AppendLine($"  • {book.Title} - {book.Author}");
                }
            }

            sb.AppendLine("\nПосоветуй 3 книги, которые мне должны понравиться, с учетом моих предпочтений.");
            sb.AppendLine("Обязательно указывай ссылки на поиск на Litmir!");

            return sb.ToString();
        }

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
                        ResponseToken Token = JsonConvert.DeserializeObject<ResponseToken>(ResponseContent);
                        _logger.LogInformation("Токен успешно получен");
                        return Token.access_token;
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

        private async Task EnsureTokenAsync()
        {
            if (Token == null || TokenExpirationTime <= DateTime.UtcNow)
            {
                Token = await GetToken();
                TokenExpirationTime = DateTime.UtcNow.AddMinutes(30);
                _logger.LogInformation("Токен обновлен");
            }
        }

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

                    var DataRequest = new API_UP_02.GigaChat_LLM.For_GigaChat.Models.Request()
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
    }
    public class UserReadingContext
    {
        public List<BookInfo> WantToRead { get; set; } = new();
        public List<BookInfo> CurrentlyReading { get; set; } = new();
        public List<BookInfo> FinishedBooks { get; set; } = new();
        public List<BookInfo> FavoriteBooks { get; set; } = new();
    }

    public class BookInfo
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Genre { get; set; }
    }
}