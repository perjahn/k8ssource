using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace k8ssource.Controllers
{
    [ApiController]
    [Route("")]
    [Produces("application/json")]
    public class RootController : ControllerBase
    {
        private readonly ILogger<RootController> _logger;

        public RootController(ILogger<RootController> logger)
        {
            _logger = logger;
        }

        // GET /
        [HttpGet]
        public string Get()
        {
            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: root");

            return "ok!";
        }
    }
}
