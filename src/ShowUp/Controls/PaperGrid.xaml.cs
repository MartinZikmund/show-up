namespace ShowUp.Controls;

/// <summary>Squared-paper background: a single Path of 24 px grid lines, rebuilt only on resize.</summary>
public sealed partial class PaperGrid : UserControl
{
    private const double CellSize = 24;

    public PaperGrid()
    {
        InitializeComponent();
        SizeChanged += (_, e) => RebuildLines(e.NewSize);
    }

    private void RebuildLines(Size size)
    {
        GeometryGroup group = new();

        // Half-pixel offset keeps 1 px lines crisp at 100% scale.
        for (var x = CellSize - 0.5; x < size.Width; x += CellSize)
        {
            group.Children.Add(new LineGeometry { StartPoint = new(x, 0), EndPoint = new(x, size.Height) });
        }

        for (var y = CellSize - 0.5; y < size.Height; y += CellSize)
        {
            group.Children.Add(new LineGeometry { StartPoint = new(0, y), EndPoint = new(size.Width, y) });
        }

        Lines.Data = group;
    }
}
