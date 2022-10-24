using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace k8ssource.Controllers
{
    [ApiController]
    [Route("annotations")]
    [Produces("application/json")]
    public class AnnotationsController : ControllerBase
    {
        private readonly ILogger<AnnotationsController> _logger;

        public AnnotationsController(ILogger<AnnotationsController> logger)
        {
            _logger = logger;
        }

        // POST /annotations
        [HttpPost]
        public string Post([FromBody] string value)
        {
            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: annotations");

            return "{ \"aa\": \"11\", \"bb\": \"22\" }";
        }
    }
}
