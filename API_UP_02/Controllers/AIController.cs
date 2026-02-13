using Microsoft.AspNetCore.Mvc;
using API_UP_02.Services;
using API_UP_02.GigaChat_LLM.For_GigaChat.Models;

namespace API_UP_02.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly GigaChatService gigaChatService;
        private static Dictionary<string, List<Request.Message>> _sessions = new();

        public AIController()
        {
            gigaChatService = new GigaChatService();
        }

        [HttpPost("recommend-book")]
        public async Task<IActionResult> RecommendBook([FromForm] BookRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Query))
                    return BadRequest(new { error = "Запрос не может быть пустым", success = false });

                if (!_sessions.ContainsKey(request.SessionId))
                    _sessions[request.SessionId] = new List<Request.Message>();

                var response = await gigaChatService.GetBookRecommendation(
                    request.Query,
                    _sessions[request.SessionId]
                );

                return Ok(new
                {
                    response = response,
                    session_id = request.SessionId,
                    success = true
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, success = false });
            }
        }

        [HttpPost("simple-ask")]
        public async Task<IActionResult> SimpleAsk([FromForm] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return BadRequest(new { error = "Запрос пустой", success = false });

                var response = await gigaChatService.GetBookRecommendation(query);

                return Ok(new { response = response, success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, success = false });
            }
        }

    }

    public class BookRequest
    {
        public string Query { get; set; }
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
    }
}