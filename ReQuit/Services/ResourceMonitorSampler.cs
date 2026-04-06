using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Management;

namespace ReQuit.Services;

public readonly record struct GpuSample(int SlotIndex, string Label, float Percent);

public readonly record struct NetInterfaceSample(string InterfaceId, string Label, float Percent);

public readonly record struct ResourceSnapshot(
    float CpuPercent,
    float NetworkPercentAggregate,
    IReadOnlyList<GpuSample> GpuSamples,
    IReadOnlyList<NetInterfaceSample> NetSamples);

/// <summary>
/// CPU (GetSystemTimes), GPU (как в диспетчере задач: WMI-адаптеры + «GPU Engine», при необходимости WMI GPUEngine),
/// сеть по интерфейсам.
/// </summary>
public sealed class ResourceMonitorSampler : IDisposable
{
    private static readonly ResourceSnapshot DisposedSnapshot = new(0f, 0f, Array.Empty<GpuSample>(), Array.Empty<NetInterfaceSample>());

    private static readonly Regex PhysIndexRegex = new(@"phys_(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly object _samplerLock = new();
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    private static ulong ToUlong(FileTime ft) => ((ulong)ft.High << 32) | ft.Low;

    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _cpuPrimed;

    private readonly List<GpuAdapterCounters> _gpuAdapters = new();

    private readonly Dictionary<string, long> _netBytesById = new(StringComparer.Ordinal);
    private DateTime _netUtc = DateTime.UtcNow;
    private bool _netPrimed;
    private float _lastNetworkAggregate;
    private List<NetInterfaceSample> _lastNetSamples = new();

    /// <summary>WMI по GPU Engine тяжёлая — не вызывать каждый тик опроса (блокирует UI).</summary>
    private Dictionary<int, float>? _wmiGpuCache;
    private DateTime _wmiGpuCacheUtc;
    private NetworkInterface[]? _nicEnumCache;
    private DateTime _nicEnumCacheUtc;
    private static readonly TimeSpan WmiGpuCacheTtl = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan NicEnumCacheTtl = TimeSpan.FromSeconds(3);

    private sealed class GpuAdapterCounters
    {
        public int SlotIndex { get; init; }
        public string Label { get; init; } = "";
        public List<PerformanceCounter> Counters { get; init; } = new();
    }

    public ResourceMonitorSampler()
    {
        BuildGpuCounters();
    }

    /// <summary>Сетевые интерфейсы для списка (как в диспетчере задач: активные, не loopback).</summary>
    public static IReadOnlyList<(string Id, string Label)> DiscoverNetworkInterfaces()
    {
        var list = new List<(string Id, string Label)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var name = string.IsNullOrWhiteSpace(nic.Name) ? nic.Id : nic.Name.Trim();
                var desc = nic.Description?.Trim();
                string label = name;
                if (!string.IsNullOrEmpty(desc) && !desc.Equals(name, StringComparison.OrdinalIgnoreCase))
                    label = name + " — " + desc;

                list.Add((nic.Id, label));
            }
        }
        catch
        {
            // ignored
        }

        return list;
    }

    /// <summary>
    /// Видеокарты: счётчики «GPU Engine» привязаны к <c>phys_N</c>, где N совпадает с порядком DXGI-адаптеров (как в диспетчере задач),
    /// а не с порядком WMI Win32_VideoController. Имена берём из DXGI; при нехватке — из WMI.
    /// </summary>
    public static IReadOnlyList<GpuAdapterDiscovery> DiscoverGpuAdapters()
    {
        var list = new List<GpuAdapterDiscovery>();
        var groups = TryGroupGpuEngineInstancesByPhys();
        var sortedPhys = groups.Keys.OrderBy(x => x).ToList();
        if (sortedPhys.Count == 0)
            return list;

        var dxgiLabels = DxgiGpuEnumerator.TryGetHardwareGpuLabelsOrdered();
        var wmiNames = GetWmiDisplayAdapterNames();

        for (var i = 0; i < sortedPhys.Count; i++)
        {
            string label;
            if (i < dxgiLabels.Count)
                label = dxgiLabels[i];
            else if (i < wmiNames.Count)
                label = wmiNames[i];
            else
                label = string.Format(CultureInfo.CurrentCulture, "GPU {0}", i + 1);

            if (!groups.TryGetValue(sortedPhys[i], out var insts))
                insts = new List<string>();

            list.Add(new GpuAdapterDiscovery(i, label, insts));
        }

        return list;
    }

    private static List<string> GetWmiDisplayAdapterNames()
    {
        var list = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, Status FROM Win32_VideoController");
            using var coll = searcher.Get();
            foreach (ManagementObject mo in coll)
            {
                try
                {
                    var st = mo["Status"]?.ToString();
                    if (!string.Equals(st, "OK", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(st))
                        continue;

                    var n = mo["Name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(n))
                        continue;
                    if (n.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (n.Contains("Parsec Virtual", StringComparison.OrdinalIgnoreCase))
                        continue;

                    list.Add(n.Trim());
                }
                finally
                {
                    mo.Dispose();
                }
            }
        }
        catch
        {
            // ignored
        }

        return list;
    }

    private static Dictionary<int, List<string>> TryGroupGpuEngineInstancesByPhys()
    {
        var groups = new Dictionary<int, List<string>>();
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
                return groups;

            var cat = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = cat.GetInstanceNames();
            foreach (var name in instanceNames)
            {
                var m = PhysIndexRegex.Match(name);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var phys))
                {
                    if (!groups.TryGetValue(phys, out var g))
                    {
                        g = new List<string>();
                        groups[phys] = g;
                    }

                    g.Add(name);
                }
            }

            if (groups.Count == 0 && instanceNames.Length > 0)
                groups[0] = instanceNames.ToList();
        }
        catch
        {
            // ignored
        }

        return groups;
    }

    /// <summary>WMI: загрузка движков GPU (дублирует логику диспетчера задач на части систем).</summary>
    private static Dictionary<int, float> TryWmiGpuEngineUtilizationByPhys()
    {
        var byPhys = new Dictionary<int, float>();
        string[] queries =
        {
            "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",
            "SELECT Name, Percentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"
        };

        foreach (var q in queries)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(q);
                using var coll = searcher.Get();
                foreach (ManagementObject mo in coll)
                {
                    try
                    {
                        var name = mo["Name"]?.ToString();
                        if (string.IsNullOrEmpty(name))
                            continue;

                        float u = 0;
                        if (mo["UtilizationPercentage"] != null)
                            u = ToFloat(mo["UtilizationPercentage"]);
                        else if (mo["Percentage"] != null)
                            u = ToFloat(mo["Percentage"]);

                        var m = PhysIndexRegex.Match(name);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var phys))
                        {
                            if (!byPhys.TryGetValue(phys, out var cur) || u > cur)
                                byPhys[phys] = u;
                        }
                    }
                    finally
                    {
                        mo.Dispose();
                    }
                }

                if (byPhys.Count > 0)
                    return byPhys;
            }
            catch
            {
                // try next query
            }
        }

        return byPhys;
    }

    private Dictionary<int, float> GetWmiGpuEngineUtilizationCached()
    {
        var now = DateTime.UtcNow;
        if (_wmiGpuCache != null && (now - _wmiGpuCacheUtc) < WmiGpuCacheTtl)
            return _wmiGpuCache;
        _wmiGpuCache = TryWmiGpuEngineUtilizationByPhys();
        _wmiGpuCacheUtc = now;
        return _wmiGpuCache;
    }

    private NetworkInterface[] GetNetworkInterfacesCached()
    {
        var now = DateTime.UtcNow;
        if (_nicEnumCache != null && (now - _nicEnumCacheUtc) < NicEnumCacheTtl)
            return _nicEnumCache;
        _nicEnumCache = NetworkInterface.GetAllNetworkInterfaces();
        _nicEnumCacheUtc = now;
        return _nicEnumCache;
    }

    private static float ToFloat(object? o)
    {
        try
        {
            return Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0f;
        }
    }

    private void BuildGpuCounters()
    {
        foreach (var d in DiscoverGpuAdapters())
        {
            var counters = new List<PerformanceCounter>();
            foreach (var inst in d.InstanceNames)
            {
                PerformanceCounter? pc = null;
                try
                {
                    pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                    _ = pc.NextValue();
                    counters.Add(pc);
                    pc = null;
                }
                catch
                {
                    pc?.Dispose();
                }
            }

            _gpuAdapters.Add(new GpuAdapterCounters
            {
                SlotIndex = d.SlotIndex,
                Label = d.DisplayLabel,
                Counters = counters
            });
        }
    }

    public ResourceSnapshot ReadSnapshot()
    {
        lock (_samplerLock)
        {
            if (_disposed)
                return DisposedSnapshot;

            var wmiPhys = GetWmiGpuEngineUtilizationCached();
            var gpuList = new List<GpuSample>();
            var sortedPhys = wmiPhys.Keys.OrderBy(x => x).ToList();

            foreach (var ad in _gpuAdapters)
            {
                double mx = 0;
                foreach (var c in ad.Counters)
                {
                    try
                    {
                        mx = Math.Max(mx, c.NextValue());
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (mx < 0.01 && sortedPhys.Count > 0)
                {
                    var physIdx = ad.SlotIndex < sortedPhys.Count ? sortedPhys[ad.SlotIndex] : sortedPhys[Math.Min(ad.SlotIndex, sortedPhys.Count - 1)];
                    if (wmiPhys.TryGetValue(physIdx, out var wu))
                        mx = wu;
                }

                gpuList.Add(new GpuSample(ad.SlotIndex, ad.Label, (float)Math.Clamp(mx, 0, 100)));
            }

            var net = ReadNetworkSamples(out var aggregate);
            return new ResourceSnapshot(ReadCpuPercent(), aggregate, gpuList, net);
        }
    }

    private IReadOnlyList<NetInterfaceSample> ReadNetworkSamples(out float aggregate)
    {
        aggregate = 0;
        var samples = new List<NetInterfaceSample>();
        var now = DateTime.UtcNow;
        var nics = GetNetworkInterfacesCached();

        if (!_netPrimed)
        {
            foreach (var nic in nics)
            {
                if (!IsActiveNic(nic))
                    continue;
                try
                {
                    var st = nic.GetIPStatistics();
                    _netBytesById[nic.Id] = st.BytesReceived + st.BytesSent;
                }
                catch
                {
                    // ignored
                }
            }

            _netUtc = now;
            _netPrimed = true;
            _lastNetworkAggregate = 0f;
            return samples;
        }

        var dt = (now - _netUtc).TotalSeconds;
        if (dt < 0.08)
        {
            aggregate = _lastNetworkAggregate;
            return _lastNetSamples;
        }

        double maxPct = 0;

        foreach (var nic in nics)
        {
            if (!IsActiveNic(nic))
                continue;

            long cur;
            try
            {
                var st = nic.GetIPStatistics();
                cur = st.BytesReceived + st.BytesSent;
            }
            catch
            {
                continue;
            }

            if (!_netBytesById.TryGetValue(nic.Id, out var prev))
            {
                _netBytesById[nic.Id] = cur;
                continue;
            }

            var delta = cur - prev;
            if (delta < 0)
                delta = 0;
            _netBytesById[nic.Id] = cur;

            var speed = nic.Speed;
            float pct = 0;
            if (speed > 0)
            {
                var rateBps = delta * 8.0 / dt;
                pct = (float)Math.Clamp(100.0 * rateBps / speed, 0, 100);
            }

            if (pct > maxPct)
                maxPct = pct;

            var label = string.IsNullOrWhiteSpace(nic.Name) ? nic.Id : nic.Name.Trim();
            samples.Add(new NetInterfaceSample(nic.Id, label, pct));
        }

        _netUtc = now;
        aggregate = (float)maxPct;
        _lastNetworkAggregate = aggregate;
        _lastNetSamples = samples;
        return samples;
    }

    public static float GetPercentForKey(string resourceKey, ResourceSnapshot s)
    {
        if (string.Equals(resourceKey, ResourceTriggerKeys.Cpu, StringComparison.OrdinalIgnoreCase))
            return s.CpuPercent;
        if (string.Equals(resourceKey, ResourceTriggerKeys.Internet, StringComparison.OrdinalIgnoreCase))
            return s.NetworkPercentAggregate;

        if (resourceKey.StartsWith("gpu:", StringComparison.Ordinal)
            && int.TryParse(resourceKey.AsSpan(4), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var slot))
        {
            foreach (var g in s.GpuSamples)
            {
                if (g.SlotIndex == slot)
                    return g.Percent;
            }

            return 0f;
        }

        if (resourceKey.StartsWith("net:", StringComparison.Ordinal))
        {
            var id = resourceKey.Length > 4 ? resourceKey.Substring(4) : "";
            foreach (var n in s.NetSamples)
            {
                if (string.Equals(n.InterfaceId, id, StringComparison.OrdinalIgnoreCase))
                    return n.Percent;
            }

            return 0f;
        }

        return 0f;
    }

    private float ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            return 0f;

        var idle = ToUlong(idleFt);
        var kernel = ToUlong(kernelFt);
        var user = ToUlong(userFt);

        if (!_cpuPrimed)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _cpuPrimed = true;
            return 0f;
        }

        var idleDiff = idle - _prevIdle;
        var kernelDiff = kernel - _prevKernel;
        var userDiff = user - _prevUser;

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        var total = kernelDiff + userDiff;
        if (total == 0)
            return 0f;

        var busy = total - idleDiff;
        if (busy < 0)
            busy = 0;

        return (float)(100.0 * busy / total);
    }

    private static bool IsActiveNic(NetworkInterface nic) =>
        nic.OperationalStatus == OperationalStatus.Up
        && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback;

    public void Dispose()
    {
        lock (_samplerLock)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var ad in _gpuAdapters)
            {
                foreach (var c in ad.Counters)
                {
                    try
                    {
                        c.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            _gpuAdapters.Clear();
        }
    }
}

public readonly record struct GpuAdapterDiscovery(int SlotIndex, string DisplayLabel, IReadOnlyList<string> InstanceNames);

public static class ResourceTriggerKeys
{
    public const string Cpu = "cpu";
    public const string Internet = "internet";

    public static string Gpu(int slotIndex) => "gpu:" + slotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string NetInterface(string interfaceId) => "net:" + interfaceId;
}
