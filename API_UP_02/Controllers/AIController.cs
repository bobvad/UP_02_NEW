// Controllers/AIController.cs
using API_UP_02.GigaChat_LLM.For_GigaChat.Models;
using API_UP_02.Services;
using Microsoft.AspNetCore.Mvc;

namespace API_UP_02.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "v2")]
    public class AIController : ControllerBase
    {
        private readonly GigaChatService _gigaChatService;
        private static Dictionary<string, List<Request.Message>> _sessions = new();

        public AIController(GigaChatService gigaChatService)
        {
            _gigaChatService = gigaChatService;
        }

        [HttpPost("simple-ask")]
        public async Task<IActionResult> SimpleAsk([FromForm] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return BadRequest(new { error = "Запрос пустой", success = false });

                var response = await _gigaChatService.GetBookRecommendation(query);

                return Ok(new { response = response, success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, success = false });
            }
        }

        [HttpGet("personal/{userId}")]
        public async Task<IActionResult> GetPersonalRecommendation(int userId)
        {
            try
            {
                var recommendation = await _gigaChatService.GetPersonalizedRecommendation(userId);
                return Ok(new { recommendation = recommendation, success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, success = false });
            }
        }

        [HttpGet("auto/{userId}")]
        public async Task<IActionResult> GetAutoRecommendation(int userId)
        {
            try
            {
                var recommendation = await _gigaChatService.GetAutoRecommendation(userId);

                if (recommendation == null)
                {
                    return Ok(new
                    {
                        message = "Новых рекомендаций пока нет",
                        show = false,
                        success = true
                    });
                }

                return Ok(new
                {
                    recommendation = recommendation,
                    show = true,
                    success = true
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, success = false });
            }
        }
    }
}