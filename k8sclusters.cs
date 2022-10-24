using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using k8s;
using k8s.Autorest;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Extensions.Logging;

public class Pod
{
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    public string[] Images { get; set; } = new string[] { };
    public Container[] Containers { get; set; } = new Container[] { };
    public Status Status { get; set; } = new Status { };
}

public class Container
{
    public string[] Args { get; set; } = new string[] { };
    public string[] Command { get; set; } = new string[] { };
    public string Image { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string WorkingDir { get; set; } = string.Empty;
}

public class Status
{
    public ContainerStatus[] ContainerStatuses { get; set; } = new ContainerStatus[] { };
    public string Message { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = new DateTime();
}

public class ContainerStatus
{
    public string ContainerID { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ImageID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Ready { get; set; } = false;
    public int RestartCount { get; set; } = 0;
    public bool Started { get; set; } = false;
    public State State { get; set; } = new State();
}

public class State
{
    public StateRunning StateRunning { get; set; } = new StateRunning();
    public StateTerminated StateTerminated { get; set; } = new StateTerminated();
    public StateWaiting StateWaiting { get; set; } = new StateWaiting();
}

public class StateRunning
{
    public DateTime StartedAt { get; set; } = new DateTime();
}

public class StateTerminated
{
    public string ContainerID { get; set; } = string.Empty;
    public int ExitCode { get; set; } = 0;
    public DateTime FinishedAt { get; set; } = new DateTime();
    public string Message { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = new DateTime();
}

public class StateWaiting
{
    public string Message { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class TableRow
{
    public string Name { get; set; } = string.Empty;
    public string[][] Data { get; set; } = { };
    public bool Different { get; set; } = false;
}

class k8sclusters
{
    private readonly ILogger<k8sclusters> _logger;

    public k8sclusters()
    {
        var factory = LoggerFactory.Create(b => b.AddConsole());
        var logger = factory.CreateLogger<k8sclusters>();
        _logger = logger;
    }

    public k8sclusters(ILogger<k8sclusters> logger)
    {
        _logger = logger;
    }

    public string[] GetClusters()
    {
        int timeoutSeconds = Config.Timeout;

        var config = KubernetesClientConfiguration.LoadKubeConfig();
        var clusters = new List<string>();

        foreach (var context in config.Contexts.OrderBy(c => c.Name))
        {
            string cluster = context.Name;

            KubernetesClientConfiguration clientConfig;
            try
            {
                clientConfig = KubernetesClientConfiguration.BuildConfigFromConfigObject(config, cluster);
            }
            catch (KubeConfigException ex)
            {
                _logger.LogWarning($"{DateTime.UtcNow:HH:mm:ss}: Ignoring cluster (invalid config): '{cluster}': {ex.Message}");
                continue;
            }

            clusters.Add(cluster);
        }

        return clusters.ToArray();
    }

    public async Task<TableRow[]> GetContainersAsync(string[] clusters)
    {
        var pods = (await GetAllPodsAsync(clusters)).ToArray();

        _logger.LogInformation($"{DateTime.UtcNow:HH:mm:ss}: Got {pods.Length} pods.");

        if (pods.Length == 0)
        {
            return Array.Empty<TableRow>();
        }

        var containers = pods.SelectMany(p => p.Containers).Select(c => c.Name).Distinct().OrderBy(n => n).ToArray();

        _logger.LogInformation($"{DateTime.UtcNow:HH:mm:ss}: Got {containers.Length} containers.");

        var rows = new TableRow[containers.Length + 1];

        rows[0] = new TableRow();
        rows[0].Name = "Container";
        rows[0].Data = new string[clusters.Length][];
        rows[0].Different = false;
        for (int col = 0; col < clusters.Length; col++)
        {
            rows[0].Data[col] = new[] { clusters[col] };
        }
        for (var row = 0; row < containers.Length; row++)
        {
            rows[row + 1] = new TableRow();
            rows[row + 1].Name = containers[row];
            rows[row + 1].Data = new string[clusters.Length][];
            for (int col = 0; col < clusters.Length; col++)
            {
                string container = containers[row];
                string environment = clusters[col];
                rows[row + 1].Data[col] = GetContainerVersions(pods.ToArray(), container, clusters[col]);
            }
            rows[row + 1].Different = false;
        }

        return rows;
    }

    async Task<List<Pod>> GetAllPodsAsync(string[] clusters)
    {
        var config = KubernetesClientConfiguration.LoadKubeConfig();
        var actualClusters = new List<string>();

        foreach (var context in config.Contexts.OrderBy(c => c.Name))
        {
            if (clusters.Contains(context.Name))
            {
                actualClusters.Add(context.Name);
            }
        }

        var allpods = new List<Task<List<Pod>>>();

        foreach (var cluster in actualClusters)
        {
            KubernetesClientConfiguration clientConfig;
            try
            {
                clientConfig = KubernetesClientConfiguration.BuildConfigFromConfigObject(config, cluster);
            }
            catch (KubeConfigException ex)
            {
                Console.WriteLine($"Ignoring cluster (invalid config): '{cluster}': {ex.Message}");
                continue;
            }

            Console.WriteLine($"Connecting to: {clientConfig.Host} ({cluster})");
            IKubernetes client = new Kubernetes(clientConfig);

            allpods.Add(GetPodsAsync(client, cluster));
        }

        await Task.WhenAll(allpods);

        return allpods.SelectMany(t => t.Result).ToList();
    }

    async Task<List<Pod>> GetPodsAsync(IKubernetes client, string clusterName)
    {
        V1PodList pods;
        var newList = new List<Pod>();
        try
        {
            var task = client.CoreV1.ListPodForAllNamespacesAsync();
            if (await Task.WhenAny(task, Task.Delay(Config.Timeout * 1000)) == task)
            {
                pods = await task;
            }
            else
            {
                Console.WriteLine($"Ignoring cluster: '{clusterName}': Timeout.");
                return newList;
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException || ex is HttpOperationException || ex is HttpRequestException)
        {
            Console.WriteLine($"Ignoring cluster: '{clusterName}': {ex.Message}");
            return newList;
        }

        foreach (var pod in pods.Items)
        {
            var newPod = new Pod();
            newPod.Name = pod.Metadata.Name;
            newPod.Namespace = pod.Metadata.NamespaceProperty;
            newPod.Cluster = clusterName;
            newPod.Annotations = pod.Annotations()?.ToDictionary(t => t.Key, t => t.Value) ?? new Dictionary<string, string>();
            newPod.Labels = pod.Labels()?.ToDictionary(t => t.Key, t => t.Value) ?? new Dictionary<string, string>();
            newPod.Status = new Status
            {
                Message = pod.Status.Message,
                Reason = pod.Status.Reason,
                StartTime = pod.Status.StartTime ?? new DateTime(),
                ContainerStatuses = pod.Status.ContainerStatuses?.Select(cs => new ContainerStatus
                {
                    ContainerID = cs.ContainerID,
                    Image = cs.Image,
                    ImageID = cs.ImageID,
                    Name = cs.Name,
                    Ready = cs.Ready,
                    RestartCount = cs.RestartCount,
                    Started = cs.Started ?? false,
                    State = new State
                    {
                        StateRunning = new StateRunning
                        {
                            StartedAt = cs.State.Running?.StartedAt ?? new DateTime()
                        },
                        StateTerminated = new StateTerminated
                        {
                            ContainerID = cs.State.Terminated?.ContainerID ?? string.Empty,
                            ExitCode = cs.State.Terminated?.ExitCode ?? 0,
                            FinishedAt = cs.State.Terminated?.FinishedAt ?? new DateTime(),
                            Message = cs.State.Terminated?.Message ?? string.Empty,
                            Reason = cs.State.Terminated?.Reason ?? string.Empty,
                            StartedAt = cs.State.Terminated?.StartedAt ?? new DateTime()
                        },
                        StateWaiting = new StateWaiting
                        {
                            Message = cs.State.Waiting?.Message ?? string.Empty,
                            Reason = cs.State.Waiting?.Reason ?? string.Empty
                        }
                    }
                })?.ToArray() ?? new ContainerStatus[] { }
            };
            newPod.Containers = pod.Spec.Containers.Select(c => new Container
            {
                Args = c.Args?.Select(a => a)?.ToArray() ?? new string[] { },
                Command = c.Command?.Select(c => c)?.ToArray() ?? new string[] { },
                Image = c.Image,
                Name = c.Name,
                WorkingDir = c.WorkingDir
            }).ToArray();

            newList.Add(newPod);
        }

        return newList;
    }

    string[] GetContainerVersions(Pod[] pods, string container, string cluster)
    {
        var versions = new List<string>();

        bool found = false;

        foreach (var pod in pods)
        {
            if (pod.Containers.Any(c => c.Name == container) && pod.Cluster == cluster)
            {
                var containers = pod.Containers.Where(c => c.Name == container).ToArray();

                foreach (var c in containers)
                {
                    var start = c.Image.IndexOf(':');
                    string version;
                    if (start >= 0)
                    {
                        var end = c.Image.IndexOf('@', start + 1);
                        if (end >= 0)
                        {
                            version = c.Image.Substring(start + 1, end - start - 1);
                        }
                        else
                        {
                            version = c.Image.Substring(start + 1);
                        }
                    }
                    else
                    {
                        version = c.Image;
                    }

                    if (version.StartsWith('v'))
                    {
                        version = version.Substring(1);
                    }
                    versions.Add(version);
                }

                found = true;
            }
        }

        if (versions.Count == 0 && found)
        {
            versions.Add(".");
        }

        var versionsArray = versions.Distinct().ToArray();

        Array.Sort(versionsArray, CompareVersions);

        return versionsArray;
    }

    int CompareVersions(string version1, string version2)
    {
        int result = Compare(version1, version2);
        return result;
    }

    /*
    Returns a signed integer that indicates the relative values of version1 and version2:
    - -1: version1 is less than version2
    - 0: version1 is equal to version2
    - 1: version1 is greater than version2
    */
    int Compare(string version1, string version2)
    {
        var separators = new char[] { '.', '-' };
        string[] v1 = version1.Split(separators);
        string[] v2 = version2.Split(separators);
        int elements = Math.Min(v1.Length, v2.Length);
        for (int i = 0; i < elements; i++)
        {
            if (int.TryParse(v1[i], out int i1) && int.TryParse(v2[i], out int i2))
            {
                if (i1 < i2)
                {
                    return -1;
                }
                if (i1 > i2)
                {
                    return 1;
                }
                if (i1 == i2)
                {
                    continue;
                }
            }

            int result = string.Compare(v1[i], v2[i], StringComparison.OrdinalIgnoreCase);
            if (result < 0)
            {
                return -1;
            }
            if (result > 0)
            {
                return 1;
            }
        }

        if (v1.Length == v2.Length)
        {
            return 0;
        }

        return v1.Length < v2.Length ? -1 : 1;
    }
}
