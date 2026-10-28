using System.ComponentModel;
using System.Windows;
using AgentSpeed.Theme;

namespace AgentSpeed.Updates;

/// <summary>Compact Fluent window for one update: what is new, then one click to install and relaunch.</summary>
internal sealed partial class UpdateWindow : Window
{
    private readonly UpdateWindowViewModel _viewModel;
    private (bool Offer, bool Retry, bool Dismiss) _layout;
    private bool _closeQueued;
    private bool _closed;

    public UpdateWindow(UpdateWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.PropertyChanged += OnViewModelChanged;
        if (AppIconSource.Frame(96) is { } frame)
        {
            AppIcon.Source = frame;
            Icon = AppIconSource.Frame(32) ?? frame;
        }
        else
        {
            AppIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => FocusPrimary();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (!_closeQueued)
        {
            _closeQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_closed)
                {
                    Close();
                }
            });
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var layout = (_viewModel.ShowOffer, _viewModel.ShowRetry, _viewModel.ShowDismiss);
        if (layout != _layout)
        {
            _layout = layout;
            Dispatcher.BeginInvoke(FocusPrimary, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void FocusPrimary()
    {
        UIElement? target = _viewModel.ShowOffer ? InstallButton : _viewModel.ShowRetry ? RetryButton : _viewModel.ShowDismiss ? DismissButton : null;
        if (IsActive && target is { IsVisible: true, IsKeyboardFocusWithin: false })
        {
            target.Focus();
        }
    }
}
