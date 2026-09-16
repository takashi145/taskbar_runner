using TaskbarRunner.Platform;
using Xunit;

// 配置の計算はデスクトップなしで実行できる。各条件をTest Explorerで個別に表示する。
public sealed class TaskbarPlacementTests
{
    [Theory]
    [MemberData(nameof(Placements))]
    public void CalculatesPlacement(string scenario, int[] rectangle, bool autoHide, uint? edge,
        uint dpi, int? expectedTop, double expectedScale)
    {
        var monitor = Rect(0, 0, 1920, 1080);
        var taskbar = Rect(rectangle[0], rectangle[1], rectangle[2], rectangle[3]);
        var success = TaskbarLocator.TryCalculatePlacement(monitor, taskbar, edge, autoHide, dpi, out var actual);
        Assert.True(success == expectedTop.HasValue, scenario);
        Assert.Equal(expectedTop is { } top ? new TaskbarPlacement(0, top, 1920, expectedScale) : default, actual);
    }

    public static TheoryData<string, int[], bool, uint?, uint, int?, double> Placements
    {
        get
        {
            int[] visible = [0, 1032, 1920, 1080];
            int[] hidden = [0, 1078, 1920, 1126];
            var data = new TheoryData<string, int[], bool, uint?, uint, int?, double>
            {
                { "Visible taskbar uses its top edge", visible, false, NativeMethods.AbeBottom, 96, 1032, 1 },
                { "Hidden taskbar uses the screen bottom", hidden, true, NativeMethods.AbeBottom, 96, 1080, 1 },
                { "Revealed auto-hide taskbar keeps its baseline", visible, true, NativeMethods.AbeBottom, 96, 1080, 1 },
                { "Disabling auto-hide restores taskbar baseline", visible, false, NativeMethods.AbeBottom, 96, 1032, 1 },
                { "Rectangle fallback supports bottom taskbar", visible, false, null, 96, 1032, 1 },
                { "Auto-hide needs no taskbar rectangle", new int[4], true, null, 96, 1080, 1 },
                { "Reject top taskbar in rectangle fallback", [0, 0, 1920, 48], false, null, 96, null, 1 },
                { "Reject taskbar above the screen bottom", [0, 900, 1920, 948], false, NativeMethods.AbeBottom, 96, null, 1 },
                { "Reject narrow taskbar in rectangle fallback", [0, 1032, 48, 1080], false, null, 96, null, 1 }
            };
            foreach (var edge in new uint[] { 0, 1, 2 })
            {
                data.Add($"Reject edge {edge} with auto-hide off", visible, false, edge, 96, null, 1);
                data.Add($"Reject edge {edge} with auto-hide on", hidden, true, edge, 96, null, 1);
            }
            foreach (var (dpi, scale) in new (uint, double)[] { (0, 1), (96, 1), (120, 1.25), (144, 1.5), (192, 2) })
            {
                data.Add($"Normal placement at DPI {dpi}", visible, false, NativeMethods.AbeBottom, dpi, 1032, scale);
                data.Add($"Auto-hide placement at DPI {dpi}", hidden, true, NativeMethods.AbeBottom, dpi, 1080, scale);
            }
            return data;
        }
    }

    private static NativeMethods.Rect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };
}
