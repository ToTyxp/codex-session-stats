using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

/// <summary>
/// The preview's text area (DESIGN 11, Consent sheet and Preview window): the pretty-printed body in a monospaced font,
/// keys in the secondary color and values in the label color, read-only and selectable. It changes in place once a second
/// and keeps its scroll position; while the user has a selection it waits, so copying a part never races an update.
/// </summary>
internal static class ReportPreviewText
{
    public static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, Courier New");

    /// <summary>A read-only rich text box for the preview, labelled for assistive technology.</summary>
    public static RichTextBox Create(string accessibleName)
    {
        var box = new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsDocumentEnabled = false,
            FontFamily = Monospace,
            FontSize = 12,
            Padding = new Thickness(6, 8, 6, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Document = new FlowDocument(new Paragraph())
            {
                PagePadding = new Thickness(0),
                FontFamily = Monospace,
                FontSize = 12,
                LineHeight = 17,
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(box, accessibleName);
        return box;
    }

    /// <summary>Replaces the text when it changed; <paramref name="header"/> lines (request line and headers) come first.</summary>
    public static void Update(RichTextBox box, IReadOnlyList<ReportJsonToken> tokens, IReadOnlyList<(string Name, string Value)>? header = null)
    {
        var text = string.Concat(tokens.Select(t => t.Text)) + string.Concat((header ?? []).Select(h => h.Name + h.Value));
        if (Equals(box.Tag, text) || !box.Selection.IsEmpty)
        {
            return;
        }

        box.Tag = text;
        // Lines never wrap; the page is as wide as the longest line, so a scroll bar appears only when a line needs it.
        var longest = string.Concat(tokens.Select(t => t.Text)).Split('\n').Concat((header ?? []).Select(h => h.Name + ": " + h.Value))
            .Max(line => line.Length);
        box.Document.PageWidth = Math.Max(240, longest * 7.4 + 24);
        var paragraph = box.Document.Blocks.FirstBlock as Paragraph ?? new Paragraph();
        paragraph.Inlines.Clear();
        foreach (var (name, value) in header ?? [])
        {
            if (name.Length > 0)
            {
                paragraph.Inlines.Add(Run(name + ": ", "TextFillColorSecondaryBrush"));
            }

            paragraph.Inlines.Add(Run(value + "\n", "TextFillColorPrimaryBrush"));
        }

        if (header is { Count: > 0 })
        {
            paragraph.Inlines.Add(new Run("\n"));
        }

        foreach (var token in tokens)
        {
            paragraph.Inlines.Add(Run(token.Text, token.Kind switch
            {
                ReportJsonTokenKind.Key => "TextFillColorSecondaryBrush",
                ReportJsonTokenKind.Punctuation => "TextFillColorTertiaryBrush",
                _ => "TextFillColorPrimaryBrush",
            }));
        }

        if (box.Document.Blocks.Count == 0)
        {
            box.Document.Blocks.Add(paragraph);
        }
    }

    private static Run Run(string text, string brush)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.ForegroundProperty, brush);
        return run;
    }
}
