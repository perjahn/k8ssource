using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace k8ssource.Controllers
{
    public class QueryRequest
    {
        public int panelId { get; set; } = 0;
        public QueryRange range { get; set; } = new QueryRange();
        public QueryRaw rangeRaw { get; set; } = new QueryRaw();
        public string interval { get; set; } = string.Empty;
        public int intervalMs { get; set; } = 0;
        public QueryTarget[] targets { get; set; } = new QueryTarget[] { };
        public QueryAdhocFilter[] adhocFilters { get; set; } = new QueryAdhocFilter[] { };
        public string format { get; set; } = string.Empty;
        public int maxDataPoints { get; set; } = 0;
    }

    public class QueryRange
    {
        public DateTime from { get; set; } = DateTime.MinValue;
        public DateTime to { get; set; } = DateTime.MinValue;
        public QueryRaw raw { get; set; } = new QueryRaw();
    }

    public class QueryRaw
    {
        public string from { get; set; } = string.Empty;
        public string to { get; set; } = string.Empty;
    }

    public class QueryTarget
    {
        public string target { get; set; } = string.Empty;
        public string refId { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
    }

    public class QueryAdhocFilter
    {
        public string key { get; set; } = string.Empty;
        [BindProperty(Name = "operator")]
        public string operatorx { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
    }


    public class QueryResult
    {
        public QueryColumns[] columns { get; set; } = Array.Empty<QueryColumns>();
        public string[][] rows { get; set; } = new[] { Array.Empty<string>() };
        public string type { get; set; } = "table";
    }

    public class QueryColumns
    {
        public string text { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("query")]
    [Produces("application/json")]
    public class QueryController : ControllerBase
    {
        private readonly ILogger<QueryController> _logger;

        public QueryController(ILogger<QueryController> logger)
        {
            _logger = logger;
        }

        // POST /query
        [HttpPost]
        public async Task<IEnumerable<QueryResult>> Post([FromBody] QueryRequest value)
        {
            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: query");

            // '(aa-dev|bb-prod|cc-test)'

            var clusters = new List<string>();

            foreach (var targetName in value.targets.Select(t => t.target))
            {
                var t = targetName;
                if (t.StartsWith('('))
                {
                    t = t.Substring(1);
                }
                if (t.EndsWith(')'))
                {
                    t = t.Substring(0, t.Length - 1);
                }
                clusters.AddRange(t.Split('|'));
            }

            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: Got {clusters.Count} clusters: '{string.Join("', '", clusters)}'");


            var k8sclusters = new k8sclusters();
            var containers = await k8sclusters.GetContainersAsync(clusters.ToArray());

            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: Got {containers.Length} containers.");


            var result = new QueryResult();

            if (containers.Length > 0)
            {
                var columns = new List<QueryColumns>();

                var col = new QueryColumns() { text = containers[0].Name, type = "string" };
                columns.Add(col);

                foreach (var datacol in containers[0].Data)
                {
                    col = new QueryColumns() { text = datacol[0], type = "string" };
                    columns.Add(col);
                }

                result.columns = columns.ToArray();
            }

            string[][] rows;

            if (containers.Length == 0)
            {
                rows = Array.Empty<string[]>();
            }
            else
            {
                rows = new string[containers.Length - 1][];

                for (int row = 1; row < containers.Length; row++)
                {
                    rows[row - 1] = new string[containers[0].Data.Length + 1];

                    for (int datacol = 0; datacol < containers[0].Data.Length + 1; datacol++)
                    {
                        if (datacol == 0)
                        {
                            rows[row - 1][datacol] = containers[row].Name;
                        }
                        else
                        {
                            rows[row - 1][datacol] = string.Join(", ", containers[row].Data[datacol - 1]);
                        }
                    }
                }
            }

            result.rows = rows;

            _logger.LogDebug($"{DateTime.UtcNow:HH:mm:ss}: Got {rows.Length} rows.");

            return new[] { result };
        }
    }
}
