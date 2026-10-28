using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// The <c>title.ladder</c> list under a redline slider (DESIGN 5): every title of a theme with its threshold, aligned in
/// a grid. Hovering or focusing a title sets <see cref="HighlightIndex"/>, which the heat strip above binds to.
/// </summary>
internal sealed partial class TitleLadderList : UserControl
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(IEnumerable),
        typeof(TitleLadderList), new PropertyMetadata(null, (d, e) => ((TitleLadderList)d).Items.ItemsSource = (IEnumerable?)e.NewValue));

    public static readonly DependencyProperty HeadingLevelProperty = DependencyProperty.Register(nameof(HeadingLevel),
        typeof(AutomationHeadingLevel), typeof(TitleLadderList),
        new PropertyMetadata(AutomationHeadingLevel.Level2,
            (d, e) => AutomationProperties.SetHeadingLevel(((TitleLadderList)d).Heading,
                (AutomationHeadingLevel)e.NewValue)));

    public static readonly DependencyProperty HighlightIndexProperty = DependencyProperty.Register(nameof(HighlightIndex), typeof(int),
        typeof(TitleLadderList), new PropertyMetadata(-1));

    public TitleLadderList()
    {
        InitializeComponent();
    }

    /// <summary>The <see cref="TitleLadderRow"/> items.</summary>
    public IEnumerable? Rows
    {
        get => (IEnumerable?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>The list heading's level for screen readers (Level2 on the General page).</summary>
    public AutomationHeadingLevel HeadingLevel
    {
        get => (AutomationHeadingLevel)GetValue(HeadingLevelProperty);
        set => SetValue(HeadingLevelProperty, value);
    }

    /// <summary>The tick of the title under the pointer or the keyboard focus; -1 for none.</summary>
    public int HighlightIndex
    {
        get => (int)GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    private void OnItemEnter(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TitleLadderRow row })
        {
            HighlightIndex = row.Index;
        }
    }

    private void OnItemLeave(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TitleLadderRow row } element && HighlightIndex == row.Index
            && !element.IsMouseOver && !element.IsKeyboardFocused)
        {
            HighlightIndex = -1;
        }
    }
}
