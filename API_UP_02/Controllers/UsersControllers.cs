using API_UP_02.Context;
using API_UP_02.Models;
using Microsoft.AspNetCore.Mvc;

namespace API_UP_02.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [ApiExplorerSettings(GroupName = "v1")]
    public class UsersControllers: Controller
    {
        ///<summary>
        ///Авторизация пользователя
        /// </summary>
        /// <remarks>Данный метод авторизирует пользователя, находит пользователя в базе данных</remarks>
        /// <response code="200">Пользователь успешно авторизован</response>
        /// <response code="500">При выполнении задачи на стороне сервера возникли ошибки</response>
        [Route("Auth")]
        [HttpPost]
        [ProducesResponseType(typeof(List<Users>), 200)]
        [ProducesResponseType(500)]
        public ActionResult Auth([FromForm] string Login, [FromForm] string Password)
        {
            if (Login == null && Password == null)
                return StatusCode(403);
            try
            {
                Users users = new BooksContext().Users.Where(x => x.Login == Login && x.Password == Password).First();
                return Json(users);
            }
            catch
            {
                return Json(500);
            }
        }
        ///<summary>
        ///Регистрация пользователя пользователя
        /// </summary>
        /// <remarks>Данный метод добавляет пользователя в базу данных</remarks>
        /// <response code="200">Пользователь успешно зарегистрирован</response>
        /// <response code="500">При выполнении задачи на стороне сервера возникли ошибки</response>
        [Route("Reg")]
        [HttpPost]
        [ProducesResponseType(typeof(List<Users>), 200)]
        [ProducesResponseType(500)]
        public ActionResult Reg([FromForm] string Login, [FromForm] string Password)
        {
            if (Login == null && Password == null)
                return StatusCode(403);
            try
            {
                using (BooksContext context = new BooksContext())
                {
                    Users users = new Users()
                    {
                        Login = Login,
                        Password = Password

                    };
                    context.Users.Add(users);
                    context.SaveChanges();
                    return Json(users);
                }
            }
            catch
            {
                return Json(500);
            }
        }
    }
}
