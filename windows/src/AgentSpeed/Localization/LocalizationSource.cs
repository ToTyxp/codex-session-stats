using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Localization;

/// <summary>Bindable view of the <see cref="Localizer"/>; raising <c>Item[]</c> refreshes every bound string live.</summary>
internal sealed class LocalizationSource : INotifyPropertyChanged
{
    private Localizer? _localizer;

    public static LocalizationSource Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public Localizer Localizer => _localizer ?? throw new InvalidOperationException("Localization is not initialized.");

    public string this[string key] => _localizer?.Get(key) ?? key;

    public void Attach(Localizer localizer)
    {
        if (_localizer is not null)
        {
            _localizer.LanguageChanged -= OnLanguageChanged;
        }

        _localizer = localizer;
        _localizer.LanguageChanged += OnLanguageChanged;
        OnLanguageChanged(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
}

/// <summary><c>Text="{loc:Loc settings.general}"</c></summary>
[MarkupExtensionReturnType(typeof(object))]
internal sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
