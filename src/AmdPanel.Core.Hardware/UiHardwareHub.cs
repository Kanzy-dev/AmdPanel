using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Exceptions;

namespace AmdPanel.Core.Hardware;

/// <summary>
/// UI-ориентированная обёртка над HardwareScanner / DisplayModeManager.
/// </summary>
/// <remarks>
/// Хранит <see cref="HardwareSnapshot"/> после первого сканирования и
/// переиспользует его для всех VM. Пересканирование выполняется явно через
/// ScanAsync() или автоматически при изменении выбранного GPU.
/// </remarks>
/// <para>
/// <b>Расположение</b>: ранее этот класс жил в <c>AmdPanel.App.Services</c>,
/// но перенесён в <c>AmdPanel.Core.Hardware</c>, чтобы его могли использовать
/// <c>AmdPanel.Core.Profiles</c> и <c>AmdPanel.Core.DriverManager</c> без
/// циклической зависимости на WPF-сборку <c>AmdPanel.App</c>.
/// </para>
public sealed class UiHardwareHub
{
    private readonly AdlxContext _ctx;
    private readonly HardwareScanner _scanner;
    private readonly DisplayModeManager _displayModeManager;
    private HardwareSnapshot? _snapshot;

    public UiHardwareHub(AdlxContext ctx)
    {
        _ctx = ctx;
        _scanner = new HardwareScanner(ctx);
        _displayModeManager = new DisplayModeManager(ctx);
    }

    public HardwareSnapshot Snapshot
        => _snapshot ?? throw new InvalidOperationException("Snapshot not loaded; call ScanAsync first.");

    public DisplayModeManager DisplayModeManager => _displayModeManager;
    public AdlxContext Context => _ctx;
    public HardwareScanner Scanner => _scanner;

    public async Task<HardwareSnapshot> ScanAsync()
    {
        return await Task.Run(() =>
        {
            _snapshot = _scanner.Scan();
            return _snapshot;
        }).ConfigureAwait(true);
    }

    public bool HasSnapshot => _snapshot is not null;

    /// <summary> Список GPU, отфильтрованный по VEN_1002 (только AMD). </summary>
    public IReadOnlyList<GpuInfo> GetAmdGpus()
        => Snapshot.Gpus.Where(g => g.PciVendorId == 0x1002).ToList();

    /// <summary>
    /// Список дисплеев, доступных для выбранного GPU. ADLX не предоставляет
    /// прямой GPU→Display маппинг, поэтому возвращаем все доступные дисплеи:
    /// если выбран дискретный GPU, а монитор подключён к iGPU, ADLX сообщит
    /// об этом через ConnectionState. UI фильтрует по Connected==true.
    /// </summary>
    public IReadOnlyList<DisplayInfo> GetActiveDisplays()
        => Snapshot.Displays.Where(d => d.Connected).ToList();

    public (GpuCapabilities Caps, GpuTuningRanges Ranges) GetGpuDetails(int gpuIndex)
    {
        if (!Snapshot.GpuDetails.TryGetValue(gpuIndex, out var details))
            throw new CapabilityNotSupportedException($"GPU index {gpuIndex} not found in snapshot");
        return details;
    }
}
