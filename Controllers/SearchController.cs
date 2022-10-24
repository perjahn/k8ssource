using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace k8ssource.Controllers
{
    public class SearchRequest
    {
        public string target { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("search")]
    [Produces("application/json")]
    public class SearchController : ControllerBase
    {
        private readonly ILogger<SearchController> _logger;

        public SearchController(ILogger<SearchController> logger)
        {
            _logger = logger;
        }

        // POST /search
        [HttpPost]
        public IEnumerable<string> Post([FromBody] SearchRequest value)
        {
            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: search");
            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: {value.target}");

            var k8sclusters = new k8sclusters();

            var clusters = k8sclusters.GetClusters();

            return clusters;
        }
    }
}
