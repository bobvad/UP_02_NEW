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