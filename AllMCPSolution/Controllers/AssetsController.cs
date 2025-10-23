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
            var basePath = !string.IsNullOrEmpty(_env.WebRootPath)
                ? _env.WebRootPath!
                : _env.ContentRootPath;

            var path = Path.Combine(basePath, "assets", "logo.png");

            if (!System.IO.File.Exists(path))
            {
                // Fall back to the legacy location in case the asset hasn't been moved yet
                path = Path.Combine(_env.ContentRootPath, "Views", "Shared", "Logo.png");

                if (!System.IO.File.Exists(path))
                {
                    return NotFound();
                }
            }

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "image/png");
        }
    }
}