using Microsoft.UI.Xaml.Media.Imaging;

namespace ShowUp.Controls;

/// <summary>
/// Plain paper with a faint grain: a 256 px tileable PNG laid out as a grid of images, rebuilt only when the
/// window needs more tiles or the theme swaps the texture. Nothing runs per frame.
/// </summary>
public sealed partial class PaperBackground : UserControl
{
    private const double TileSize = 256;

    private readonly List<Image> _tiles = [];
    private BitmapImage? _bitmap;
    private (int Columns, int Rows) _grid;

    public PaperBackground()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Layout();
        GrainSource.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Rebuild());
        Loaded += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Grain.Children.Clear();
        _tiles.Clear();
        _grid = (0, 0);
        _bitmap = GrainSource.Text.StartsWith("ms-appx:", StringComparison.Ordinal) ? new BitmapImage(new Uri(GrainSource.Text)) : null;
        Layout();
    }

    private void Layout()
    {
        if (_bitmap is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        Grain.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
        var columns = (int)Math.Ceiling(ActualWidth / TileSize);
        var rows = (int)Math.Ceiling(ActualHeight / TileSize);
        if (columns <= _grid.Columns && rows <= _grid.Rows)
        {
            return;
        }

        // Only ever grows: shrinking the window just clips the tiles already there.
        (columns, rows) = (Math.Max(columns, _grid.Columns), Math.Max(rows, _grid.Rows));
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (column < _grid.Columns && row < _grid.Rows)
                {
                    continue;
                }

                Image tile = new() { Source = _bitmap, Width = TileSize, Height = TileSize, Stretch = Stretch.Fill };
                Canvas.SetLeft(tile, column * TileSize);
                Canvas.SetTop(tile, row * TileSize);
                Grain.Children.Add(tile);
                _tiles.Add(tile);
            }
        }

        _grid = (columns, rows);
    }
}
