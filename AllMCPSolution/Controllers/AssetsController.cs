using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers
{
    [ApiController]
    public class AssetsController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public AssetsController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpGet("/assets/logo.png")]
        public IActionResult GetLogo()
        {
            var path = Path.Combine(_env.ContentRootPath, "Views", "Shared", "Logo.png");
            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "image/png");
        }
    }
}