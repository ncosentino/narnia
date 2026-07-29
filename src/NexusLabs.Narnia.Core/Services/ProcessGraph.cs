using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

internal sealed class ProcessGraph
{
    private const int MaxTreeDepth = 64;
    private readonly IReadOnlyDictionary<int, ProcessResourceRecord> _processes;
    private readonly IReadOnlyDictionary<int, ProcessUsage> _usage;
    private readonly IReadOnlyDictionary<int, int> _parentByChild;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<int>> _children;

    public ProcessGraph(
        IReadOnlyCollection<ProcessResourceRecord> processes,
        IReadOnlyDictionary<int, ProcessUsage> usage)
    {
        _processes = processes
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.Last());
        _usage = usage;

        var candidateParents = new Dictionary<int, int>();
        foreach (var process in _processes.Values)
        {
            if (!_processes.TryGetValue(process.ParentProcessId, out var parent) ||
                !IsValidParentChild(parent, process))
            {
                continue;
            }

            candidateParents[process.ProcessId] = parent.ProcessId;
        }

        _parentByChild = candidateParents
            .Where(pair => !HasParentCycle(pair.Key, candidateParents))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var children = new Dictionary<int, List<int>>();
        foreach (var (childProcessId, parentProcessId) in _parentByChild)
        {
            if (!_processes.ContainsKey(childProcessId))
                continue;
            if (!children.TryGetValue(parentProcessId, out var childIds))
            {
                childIds = [];
                children[parentProcessId] = childIds;
            }

            childIds.Add(childProcessId);
        }

        _children = children.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<int>)pair.Value);
    }

    public IReadOnlyList<ProcessResourceRecord> FindByName(string name) =>
        _processes.Values
            .Where(process => string.Equals(process.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(process => process.ProcessId)
            .ToArray();

    public int? FindOwningTerminal(int processId)
    {
        var visited = new HashSet<int>();
        var currentId = processId;
        for (var depth = 0; depth < MaxTreeDepth && visited.Add(currentId); depth++)
        {
            if (!_processes.TryGetValue(currentId, out var current))
                return null;

            if (string.Equals(current.Name, "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase))
                return current.ProcessId;

            if (!_parentByChild.TryGetValue(current.ProcessId, out var parentProcessId) ||
                !_processes.TryGetValue(parentProcessId, out var parent))
            {
                return null;
            }

            currentId = parent.ProcessId;
        }

        return null;
    }

    public bool HasAncestorNamed(int processId, string name)
    {
        var visited = new HashSet<int> { processId };
        var currentId = processId;
        for (var depth = 0; depth < MaxTreeDepth; depth++)
        {
            if (!_parentByChild.TryGetValue(currentId, out var parentProcessId) ||
                !_processes.TryGetValue(parentProcessId, out var parent) ||
                !visited.Add(parent.ProcessId))
            {
                return false;
            }

            if (string.Equals(parent.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;

            currentId = parent.ProcessId;
        }

        return false;
    }

    public IReadOnlyList<ProcessDescriptor> GetLaunchChain(int processId, int? terminalProcessId)
    {
        var nearestFirst = new List<ProcessDescriptor>();
        var visited = new HashSet<int> { processId };
        var currentId = processId;

        for (var depth = 0; depth < MaxTreeDepth; depth++)
        {
            if (!_processes.TryGetValue(currentId, out var current) ||
                !_parentByChild.TryGetValue(current.ProcessId, out var parentProcessId) ||
                !_processes.TryGetValue(parentProcessId, out var parent) ||
                !visited.Add(parent.ProcessId))
            {
                break;
            }

            if (terminalProcessId == parent.ProcessId ||
                string.Equals(parent.Name, "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            nearestFirst.Add(CreateDescriptor(parent.ProcessId));
            currentId = parent.ProcessId;
        }

        nearestFirst.Reverse();
        return nearestFirst;
    }

    public ProcessTreeNode BuildTree(int processId)
    {
        var visited = new HashSet<int>();
        return BuildTree(processId, visited, 0);
    }

    public IReadOnlySet<int> GetSubtreeIds(int processId)
    {
        var result = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(processId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!result.Add(current))
                continue;

            if (!_children.TryGetValue(current, out var childIds))
                continue;

            foreach (var childId in childIds)
                pending.Push(childId);
        }

        return result;
    }

    public ProcessUsage Aggregate(IEnumerable<int> processIds)
    {
        var unique = processIds.Distinct().ToArray();
        if (unique.Length == 0)
            return EmptyUsage();

        var cpu = 0d;
        var cpuSamples = 0;
        long privateBytes = 0;
        long workingSetBytes = 0;
        var represented = 0;

        foreach (var processId in unique)
        {
            if (!_usage.TryGetValue(processId, out var usage))
                continue;

            represented += usage.ProcessCount;
            cpuSamples += usage.CpuSampledProcessCount;
            if (usage.CpuPercent is not null)
                cpu += usage.CpuPercent.Value;
            privateBytes = SaturatingAdd(privateBytes, usage.PrivateBytes);
            workingSetBytes = SaturatingAdd(workingSetBytes, usage.WorkingSetBytes);
        }

        return new ProcessUsage(
            cpuSamples == 0 ? null : Math.Clamp(cpu, 0d, 100d),
            cpuSamples,
            represented,
            privateBytes,
            workingSetBytes);
    }

    public ProcessResourceRecord? GetProcess(int processId) =>
        _processes.GetValueOrDefault(processId);

    private ProcessTreeNode BuildTree(int processId, HashSet<int> visited, int depth)
    {
        if (!_processes.TryGetValue(processId, out var process))
            throw new InvalidOperationException($"Process {processId} is not present in the sample.");

        visited.Add(processId);
        var children = new List<ProcessTreeNode>();
        if (depth < MaxTreeDepth && _children.TryGetValue(processId, out var childIds))
        {
            foreach (var childId in childIds)
            {
                if (visited.Contains(childId))
                    continue;
                children.Add(BuildTree(childId, visited, depth + 1));
            }
        }

        children.Sort(static (left, right) =>
        {
            var cpu = Nullable.Compare(right.TreeUsage.CpuPercent, left.TreeUsage.CpuPercent);
            if (cpu != 0)
                return cpu;
            var memory = right.TreeUsage.PrivateBytes.CompareTo(left.TreeUsage.PrivateBytes);
            return memory != 0 ? memory : left.ProcessId.CompareTo(right.ProcessId);
        });

        var own = _usage.GetValueOrDefault(processId, EmptyUsage());
        var tree = Aggregate([processId, .. children.SelectMany(GetNodeIds)]);
        return new ProcessTreeNode(
            process.ProcessId,
            process.ParentProcessId,
            process.Name,
            process.StartedAt,
            own,
            tree,
            children);
    }

    private ProcessDescriptor CreateDescriptor(int processId)
    {
        var process = _processes[processId];
        return new ProcessDescriptor(
            process.ProcessId,
            process.ParentProcessId,
            process.Name,
            process.StartedAt,
            _usage.GetValueOrDefault(processId, EmptyUsage()));
    }

    private static IEnumerable<int> GetNodeIds(ProcessTreeNode node)
    {
        yield return node.ProcessId;
        foreach (var child in node.Children)
        {
            foreach (var processId in GetNodeIds(child))
                yield return processId;
        }
    }

    private static bool IsValidParentChild(
        ProcessResourceRecord parent,
        ProcessResourceRecord child) =>
        parent.ProcessId != child.ProcessId &&
        parent.StartedAt is not null &&
        child.StartedAt is not null &&
        child.StartedAt.Value >= parent.StartedAt.Value;

    private static bool HasParentCycle(
        int startProcessId,
        IReadOnlyDictionary<int, int> parentByChild)
    {
        var visited = new HashSet<int>();
        var current = startProcessId;
        while (parentByChild.TryGetValue(current, out var parent))
        {
            if (!visited.Add(current))
                return true;
            current = parent;
        }

        return false;
    }

    private static ProcessUsage EmptyUsage() => new(null, 0, 0, 0, 0);

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
}
