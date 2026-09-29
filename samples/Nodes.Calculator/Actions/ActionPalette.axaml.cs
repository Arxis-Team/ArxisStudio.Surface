using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Nodes.Calculator.Actions;

/// <summary>
/// Строка палитры: раздел или действие.
/// </summary>
public sealed class PaletteRow(string text, int depth, SurfaceContextAction? action)
{
    public string Text { get; } = action == null ? $"▾ {text}" : text;

    public string? Glyph { get; } = action?.Icon as string;

    public SurfaceContextAction? Action { get; } = action;

    public bool IsSection => Action == null;

    public bool IsSelectable => Action is { IsEnabled: true };

    public Thickness Indent { get; } = new(depth * 14, 0, 0, 0);
}

/// <summary>
/// Палитра действий с поиском — «All Actions for this Blueprint» из Unreal Engine.
/// </summary>
/// <remarks>
/// Показывает дерево <see cref="SurfaceContextAction"/>, которое вернул поставщик действий, — разделы и
/// действия под ними. Поиск идёт по словам: каждое слово запроса должно найтись в названии, ключевых
/// словах или разделе действия, поэтому «+» находит «Сложить», а «строк выв» — «Вывести строку». Раздел
/// без найденных действий прячется. Фокус остаётся в поле поиска: стрелки двигают выбор по действиям,
/// Enter выполняет, Esc закрывает; щелчок по действию выполняет его сразу. «С учётом контекста» виден,
/// когда палитру открыл провод из пина, и оставляет только узлы, способные его принять.
/// </remarks>
public partial class ActionPalette : UserControl
{
    private IReadOnlyList<SurfaceContextAction> _actions = [];
    private Func<SurfaceContextAction, string> _keywords = _ => string.Empty;
    private Func<SurfaceContextAction, bool>? _fits;
    private List<PaletteRow> _rows = [];

    public ActionPalette()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => Rebuild();
        ContextBox.IsCheckedChanged += (_, _) => Rebuild();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        RowList.Tapped += (_, _) => Execute(RowList.SelectedItem as PaletteRow);
    }

    /// <summary>
    /// Действие выбрано или палитру закрыли — окно убирает её.
    /// </summary>
    public event EventHandler? Done;

    public void Show(
        IReadOnlyList<SurfaceContextAction> actions,
        Func<SurfaceContextAction, string> keywords,
        Func<SurfaceContextAction, bool>? fits)
    {
        _actions = actions;
        _keywords = keywords;
        _fits = fits;
        ContextBox.IsVisible = fits != null;
        ContextBox.IsChecked = true;
        SearchBox.Text = string.Empty;
        Rebuild();
    }

    public void FocusSearch() => SearchBox.Focus();

    private void Rebuild()
    {
        var words = (SearchBox.Text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var context = ContextBox.IsVisible && ContextBox.IsChecked == true ? _fits : null;
        _rows = [];
        Walk(_actions, 0, string.Empty, words, context, _rows);
        RowList.ItemsSource = _rows;
        RowList.SelectedItem = _rows.FirstOrDefault(row => row.IsSelectable);
        if (RowList.SelectedItem != null)
            RowList.ScrollIntoView(RowList.SelectedItem);
    }

    private void Walk(
        IReadOnlyList<SurfaceContextAction> actions, int depth, string path, string[] words,
        Func<SurfaceContextAction, bool>? context, List<PaletteRow> rows)
    {
        foreach (var action in actions)
        {
            if (action.IsSeparator)
                continue;

            if (action.Items.Count > 0)
            {
                var inner = new List<PaletteRow>();
                Walk(action.Items, depth + 1, $"{path} {action.Header}", words, context, inner);
                if (inner.Any(row => !row.IsSection))
                {
                    rows.Add(new PaletteRow(action.Header, depth, null));
                    rows.AddRange(inner);
                }

                continue;
            }

            if (context != null && !context(action))
                continue;

            var haystack = $"{action.Header} {_keywords(action)} {path}".ToLowerInvariant();
            if (words.All(haystack.Contains))
                rows.Add(new PaletteRow(action.Header, depth, action));
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(+1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Execute(RowList.SelectedItem as PaletteRow);
                e.Handled = true;
                break;

            case Key.Escape:
                Done?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void Move(int step)
    {
        var index = RowList.SelectedItem is PaletteRow current ? _rows.IndexOf(current) : -1;
        for (var i = index + step; i >= 0 && i < _rows.Count; i += step)
        {
            if (!_rows[i].IsSelectable)
                continue;

            RowList.SelectedItem = _rows[i];
            RowList.ScrollIntoView(_rows[i]);
            return;
        }
    }

    private void Execute(PaletteRow? row)
    {
        if (row is not { IsSelectable: true, Action: { } action })
            return;

        // Сначала закрыть: действие ставит узел и кладёт правку в историю, палитра ему уже не нужна.
        Done?.Invoke(this, EventArgs.Empty);
        action.Command?.Execute(action.CommandParameter);
    }
}
