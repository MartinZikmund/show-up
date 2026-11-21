namespace ShowUp.Core.Marking;

/// <summary>
/// The strokes of one "draw it yourself" cross, plus the settle rule: once the ink counts, a pause of
/// <see cref="SettleDelay"/> after lifting finishes it, so the user can lift between the two halves of an X.
/// </summary>
/// <remarks><see cref="Finished"/> fires on the timer's thread when the pause settles it; marshal to the UI yourself.</remarks>
public sealed class InkSession(TimeProvider time) : IDisposable
{
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(900);

    /// <summary>Moves shorter than this (normalized) are dropped, like the prototype's 0.004.</summary>
    public const float MinMoveDistance = 0.004f;

    private readonly Lock _gate = new();
    private readonly List<InkStroke> _strokes = [];
    private List<InkPoint>? _current;
    private ITimer? _settle;
    private int _generation;

    /// <summary>Raised once, when the ink settles or <see cref="Finish"/> succeeds.</summary>
    public event EventHandler? Finished;

    public IReadOnlyList<InkStroke> Strokes
    {
        get
        {
            lock (_gate)
            {
                return [.. _strokes];
            }
        }
    }

    public double Score
    {
        get
        {
            lock (_gate)
            {
                return InkScore.Of(_strokes);
            }
        }
    }

    public bool Counts => Score >= 1;

    public bool IsDrawing
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    public bool IsFinished { get; private set; }

    /// <summary>The settle timer is armed: the ink will finish unless a new stroke or Undo comes first.</summary>
    public bool IsSettling
    {
        get
        {
            lock (_gate)
            {
                return _settle is not null;
            }
        }
    }

    public void BeginStroke(InkPoint point)
    {
        lock (_gate)
        {
            if (IsFinished)
            {
                return;
            }

            CancelSettle();
            _current = [point];
            _strokes.Add(new(_current));
        }
    }

    /// <returns>Whether the point was kept (far enough from the previous one).</returns>
    public bool AddPoint(InkPoint point)
    {
        lock (_gate)
        {
            if (_current is not { } current || IsFinished)
            {
                return false;
            }

            var last = current[^1];
            float dx = point.X - last.X, dy = point.Y - last.Y;
            if (MathF.Sqrt(dx * dx + dy * dy) < MinMoveDistance)
            {
                return false;
            }

            current.Add(point);
            return true;
        }
    }

    public void EndStroke()
    {
        lock (_gate)
        {
            if (_current is null || IsFinished)
            {
                return;
            }

            _current = null;
            if (InkScore.Counts(_strokes))
            {
                CancelSettle();
                var generation = _generation;
                _settle = time.CreateTimer(_ => OnSettled(generation), null, SettleDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Removes the last stroke and cancels a pending settle, even if the rest still counts.</summary>
    public void Undo()
    {
        lock (_gate)
        {
            if (_current is not null || IsFinished || _strokes.Count == 0)
            {
                return;
            }

            CancelSettle();
            _strokes.RemoveAt(_strokes.Count - 1);
        }
    }

    /// <summary>The Done button: finishes now if the ink counts.</summary>
    public bool Finish()
    {
        lock (_gate)
        {
            if (IsFinished || _current is not null || !InkScore.Counts(_strokes))
            {
                return false;
            }

            CancelSettle();
            IsFinished = true;
        }

        Finished?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public string Encode() => InkCodec.Encode(Strokes);

    public void Dispose()
    {
        lock (_gate)
        {
            CancelSettle();
        }
    }

    private void OnSettled(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation || IsFinished || _current is not null)
            {
                return;
            }

            _settle?.Dispose();
            _settle = null;
            IsFinished = true;
        }

        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void CancelSettle()
    {
        _generation++;
        _settle?.Dispose();
        _settle = null;
    }
}
