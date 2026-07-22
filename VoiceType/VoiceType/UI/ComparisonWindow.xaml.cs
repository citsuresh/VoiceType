using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using VoiceType.Models;

namespace VoiceType.UI
{
    /// <summary>
    /// Non-modal, focusable window showing the transcript comparison history as chat-card-style
    /// entries (a "You spoke" card and a "Final text" card per entry), with word-level diff
    /// highlights. Doubles as the post-insertion "comparison popup" (newest entry appears first /
    /// on top) and as the persisted history browser. Text is selectable and copyable via the
    /// underlying read-only <see cref="RichTextBox"/> controls.
    /// </summary>
    public partial class ComparisonWindow : Window
    {
        private static readonly Brush RemovedBackground = new SolidColorBrush(Color.FromRgb(0xF8, 0xD7, 0xDA));
        private static readonly Brush ModifiedBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xC4));
        private static readonly Brush AddedBackground = new SolidColorBrush(Color.FromRgb(0xD4, 0xED, 0xDA));
        private static readonly Brush HighlightForeground = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24));

        // Tracks currently open windows so a freshly-recorded entry can be pushed onto the top of
        // whichever windows are already visible, and so callers can reuse an open window instead
        // of stacking a new one on every bulb click.
        private static readonly List<ComparisonWindow> _openWindows = new();

        public ComparisonWindow()
        {
            InitializeComponent();
            _openWindows.Add(this);
            Closed += (_, _) => _openWindows.Remove(this);
        }

        /// <summary>
        /// Optional history persistence service, set by the caller so the "Clear History" button
        /// can delete persisted entries. When not set, the button is hidden.
        /// </summary>
        public Infrastructure.History.TranscriptHistoryService? HistoryService
        {
            get => _historyService;
            set
            {
                _historyService = value;
                ClearHistoryButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private Infrastructure.History.TranscriptHistoryService? _historyService;

        /// <summary>
        /// Returns an already-open comparison/history window, if any, so callers can reuse it
        /// instead of opening a duplicate window.
        /// </summary>
        public static ComparisonWindow? GetOpenWindow() => _openWindows.LastOrDefault();

        /// <summary>
        /// Inserts a newly-recorded entry at the top of every currently open comparison/history
        /// window. Safe to call from any thread; marshals to each window's dispatcher.
        /// </summary>
        public static void NotifyNewEntry(ComparisonEntry entry)
        {
            foreach (var window in _openWindows.ToList())
            {
                window.Dispatcher.BeginInvoke(new Action(() => window.AddEntryOnTop(entry)));
            }
        }

        /// <summary>Inserts a single new entry card at the top of the currently displayed list.</summary>
        public void AddEntryOnTop(ComparisonEntry entry)
        {
            EntriesPanel.Children.Insert(0, BuildEntryCard(entry));
            EntriesScrollViewer.ScrollToTop();
        }

        /// <summary>
        /// Rebuilds the card list from the given entries (any order in, rendered newest-first).
        /// </summary>
        public void LoadEntries(IReadOnlyList<ComparisonEntry> entries)
        {
            EntriesPanel.Children.Clear();

            foreach (var entry in entries.OrderByDescending(e => e.CreatedUtc))
                EntriesPanel.Children.Add(BuildEntryCard(entry));

            EntriesScrollViewer.ScrollToTop();
        }

        /// <summary>Positions the window near the given screen point, clamped to the work area.</summary>
        public void PositionNear(Point screenPoint)
        {
            var workArea = SystemParameters.WorkArea;
            double left = screenPoint.X;
            double top = screenPoint.Y;

            if (left + Width > workArea.Right) left = workArea.Right - Width;
            if (top + Height > workArea.Bottom) top = workArea.Bottom - Height;
            if (left < workArea.Left) left = workArea.Left;
            if (top < workArea.Top) top = workArea.Top;

            Left = left;
            Top = top;
        }

        private UIElement BuildEntryCard(ComparisonEntry entry)
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE5)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel();

            var metaText = entry.CreatedUtc.ToLocalTime().ToString("g");
            if (!string.IsNullOrWhiteSpace(entry.ModelName))
                metaText += $"  \u2022  {entry.ModelName}";

            stack.Children.Add(new TextBlock
            {
                Text = metaText,
                Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x78)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });

            stack.Children.Add(BuildSubCard("You spoke", entry.SpokenText, entry.SpokenHighlights));
            stack.Children.Add(BuildSubCard("Final text", entry.FinalText, entry.FinalHighlights, topMargin: 8));

            outer.Child = stack;
            return outer;
        }

        private UIElement BuildSubCard(string label, string text, IReadOnlyList<HighlightSpan> highlights, double topMargin = 0)
        {
            var container = new StackPanel { Margin = new Thickness(0, topMargin, 0, 0) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x5B, 0xB8)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });

            var richTextBox = new RichTextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Document = BuildFlowDocument(text, highlights)
            };

            container.Children.Add(richTextBox);
            return container;
        }

        private static FlowDocument BuildFlowDocument(string text, IReadOnlyList<HighlightSpan> highlights)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            text ??= string.Empty;

            var ordered = highlights?.OrderBy(h => h.Start).ToList() ?? new List<HighlightSpan>();
            int cursor = 0;

            foreach (var span in ordered)
            {
                if (span.Start < cursor || span.Start + span.Length > text.Length) continue;

                if (span.Start > cursor)
                    paragraph.Inlines.Add(new Run(text.Substring(cursor, span.Start - cursor)));

                paragraph.Inlines.Add(new Run(text.Substring(span.Start, span.Length))
                {
                    Background = BrushForKind(span.Kind),
                    Foreground = HighlightForeground,
                    FontWeight = FontWeights.SemiBold
                });

                cursor = span.Start + span.Length;
            }

            if (cursor < text.Length)
                paragraph.Inlines.Add(new Run(text.Substring(cursor)));

            return new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        }

        private static Brush BrushForKind(HighlightKind kind) => kind switch
        {
            HighlightKind.Removed => RemovedBackground,
            HighlightKind.Modified => ModifiedBackground,
            HighlightKind.Added => AddedBackground,
            _ => Brushes.Transparent
        };

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                this,
                "Delete all persisted transcript history? This cannot be undone.",
                "Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _historyService?.ClearAll();
            LoadEntries(Array.Empty<ComparisonEntry>());
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
