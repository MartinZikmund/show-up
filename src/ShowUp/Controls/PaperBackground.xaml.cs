using Microsoft.UI.Xaml.Media.Imaging;

namespace ShowUp.Controls;

/// <summary>
/// Plain paper with a faint grain: 512 px tileable PNGs laid out as a grid of images (WinUI has no tiling brush).
/// The tile set is resized only when the window size or the theme's texture changes; nothing runs per frame.
/// </summary>
public sealed partial class PaperBackground : UserControl
{
    private const double TileSize = 512;

    // Beyond 8x8 tiles (4096 px) the grain just stops; the paper color underneath carries on.
    private const int MaxTilesPerSide = 8;

    private readonly RectangleGeometry _clip = new();
    private readonly List<Image> _tiles = [];
    private string? _source;
    private BitmapImage? _bitmap;

    public PaperBackground()
    {
        InitializeComponent();
        Grain.Clip = _clip;
        SizeChanged += (_, _) => Layout();
        GrainSource.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Rebuild());
        Loaded += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        if (GrainSource.Text == _source)
        {
            return;
        }

        _source = GrainSource.Text;
        _bitmap = _source.StartsWith("ms-appx:", StringComparison.Ordinal) ? new BitmapImage(new Uri(_source)) : null;
        Grain.Children.Clear();
        _tiles.Clear();
        Layout();
    }

    private void Layout()
    {
        if (_bitmap is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _clip.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var columns = Math.Min(MaxTilesPerSide, (int)Math.Ceiling(ActualWidth / TileSize));
        var rows = Math.Min(MaxTilesPerSide, (int)Math.Ceiling(ActualHeight / TileSize));
        var count = columns * rows;

        while (_tiles.Count < count)
        {
            Image tile = new() { Source = _bitmap, Width = TileSize, Height = TileSize, Stretch = Stretch.Fill };
            _tiles.Add(tile);
            Grain.Children.Add(tile);
        }

        while (_tiles.Count > count)
        {
            Grain.Children.Remove(_tiles[^1]);
            _tiles.RemoveAt(_tiles.Count - 1);
        }

        for (var i = 0; i < count; i++)
        {
            Canvas.SetLeft(_tiles[i], i % columns * TileSize);
            Canvas.SetTop(_tiles[i], i / columns * TileSize);
        }
    }
}
