using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Dashboard;

/// <summary>
/// The Reduce Motion stand-in for the agents spring (DESIGN section 7): the list changes height at once and only the
/// rows that come or go fade, over 0.2 s ease-out.
/// </summary>
internal sealed class RowFade(ItemsControl items)
{
    private readonly List<UIElement> _rows = [];
    private Action? _then;
    private int _generation;

    public bool IsRunning => _rows.Count > 0 || _then is not null;

    public void FadeIn(int first, int count) => Run(first, count, from: 0, to: 1, then: null);

    /// <summary>Fades the rows out, then runs <paramref name="then"/> (also when there is nothing to fade).</summary>
    public void FadeOut(int first, int count, Action then) => Run(first, count, from: 1, to: 0, then);

    /// <summary>Ends a running fade at once and runs its pending step.</summary>
    public void Finish()
    {
        _generation++;
        foreach (var row in _rows)
        {
            row.BeginAnimation(UIElement.OpacityProperty, null);
        }

        _rows.Clear();
        var then = _then;
        _then = null;
        then?.Invoke();
    }

    private void Run(int first, int count, double from, double to, Action? then)
    {
        Finish();
        var end = Math.Min(items.Items.Count, first + count);
        for (var i = Math.Max(0, first); i < end; i++)
        {
            if (items.ItemContainerGenerator.ContainerFromIndex(i) is UIElement row)
            {
                _rows.Add(row);
            }
        }

        _then = then;
        if (_rows.Count == 0)
        {
            Finish();
            return;
        }

        var duration = TimeSpan.FromSeconds(AgentsDisclosure.CrossfadeSeconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        var generation = _generation;
        for (var i = 0; i < _rows.Count; i++)
        {
            var fade = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
            if (i == _rows.Count - 1)
            {
                // Every row starts together with the same duration, so the last one completing ends the fade.
                fade.Completed += (_, _) =>
                {
                    if (generation == _generation)
                    {
                        Finish();
                    }
                };
            }

            _rows[i].BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
