using Vortice.DXGI;

namespace ReQuit.Services;

/// <summary>
/// Порядок совпадает с индексами phys_0, phys_1, … в счётчиках «GPU Engine» (как в диспетчере задач).
/// </summary>
internal static class DxgiGpuEnumerator
{
    public static List<string> TryGetHardwareGpuLabelsOrdered()
    {
        var list = new List<string>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (var i = 0; ; i++)
            {
                var r = factory.EnumAdapters1(i, out var adapter);
                if (r.Failure)
                    break;
                using (adapter)
                {
                    var desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) != AdapterFlags.None)
                        continue;

                    list.Add(desc.Description.Trim());
                }
            }
        }
        catch
        {
            // ignored
        }

        return list;
    }
}
