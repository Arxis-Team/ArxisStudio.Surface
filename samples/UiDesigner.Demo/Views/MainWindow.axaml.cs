using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio;
using UiDesigner.Demo.Context;
using UiDesigner.Demo.ViewModels;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace UiDesigner.Demo.Views;

public partial class MainWindow : Window
{
    private SurfaceHistory? _history;

    public MainWindow()
    {
        InitializeComponent();

        if (this.FindControl<ArxisStudio.Surface.UiDesigner.UiDesignerView>("Editor") is { } editor)
        {
            editor.ContextActionProviders.Add(new UiDesignerDemoContextActionsProvider());

            // Редактор не владеет коллекцией, поэтому Delete приходит запросом.
            editor.DeleteRequested += Editor_OnDeleteRequested;

            // Деревом контролов он тоже не владеет: перестановка — структурная
            // правка, и её выполняет владелец разметки. Здесь эту роль временно
            // играет само демо, в продукте её возьмёт ArxisStudio.Markup.
            editor.ReorderRequested += Editor_OnReorderRequested;

            // Набор направляющих принадлежит модели, поэтому правит его хост.
            editor.GuideChangeRequested += Editor_OnGuideChangeRequested;

            // Канал управления для проверки API вживую. Без --automation ничего
            // не поднимается: ни таймера, ни подписок, ни файлов.
            Automation.AutomationChannel.TryStart(Program.AutomationDirectory, editor, this);

            // История — готовый SurfaceHistory: он копит правки редактора, обслуживает
            // сочетания отмены и повтора, а перестановку, которую выполняет само
            // приложение, демо кладёт в тот же стек (ReorderChange).
            _history = new SurfaceHistory(editor);

            // Соглашение этого приложения: повтор — Ctrl + X, вдобавок к принятым
            // системой. Библиотека такого не навязывает: у неё по умолчанию стоят
            // платформенные сочетания, а нестандартное задаёт тот, кому оно нужно.
            editor.InputGestures.RedoGestures = new[]
            {
                new KeyGesture(Key.X, KeyModifiers.Control),
                new KeyGesture(Key.Y, KeyModifiers.Control),
                new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift)
            };

            _history.Changed += (_, _) => UpdateHistoryButtons();
            UpdateHistoryButtons();
        }

        if (this.FindControl<ComboBox>("GridStepBox") is { } gridStep)
        {
            gridStep.SelectionChanged += (_, _) => ApplyGridCellSize();
            ApplyGridCellSize();
        }
    }

    /// <summary>
    /// Применяет выбранный шаг сетки.
    /// </summary>
    /// <remarks>
    /// Шаг задаётся ресурсом, а не свойством редактора: так его подхватывает
    /// <c>ControlTheme</c> сетки, и привязка следует за ним сама — по умолчанию
    /// <c>InteractionOptions.SnapStep</c> равен <see cref="double.NaN"/>, то есть
    /// «брать шаг у сетки». Одна настройка меняет и то, что нарисовано,
    /// и то, к чему притягивается.
    /// </remarks>
    private void ApplyGridCellSize()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        Resources["Surface.Grid.CellSize"] = viewModel.GridCellSize;
    }

    private void UpdateHistoryButtons()
    {
        if (this.FindControl<Button>("UndoButton") is { } undo)
            undo.IsEnabled = _history?.CanUndo ?? false;

        if (this.FindControl<Button>("RedoButton") is { } redo)
            redo.IsEnabled = _history?.CanRedo ?? false;
    }

    private void Editor_OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (this.FindControl<ArxisStudio.Surface.UiDesigner.UiDesignerView>("Editor") is not { } editor)
            return;

        // Удаляем от больших индексов к меньшим, иначе последующие съезжают.
        var indexes = e.Targets
            .Select(target => editor.IndexFromContainer(target.Container))
            .Where(index => index >= 0)
            .Distinct()
            .OrderByDescending(index => index)
            .ToList();

        if (indexes.Count == 0)
            return;

        foreach (var index in indexes)
            viewModel.Elements.RemoveAt(index);

        e.Handled = true;
        RefreshPanels();
    }

    /// <summary>
    /// Применяет запрошенное изменение набора направляющих.
    /// </summary>
    /// <remarks>
    /// Редактор направляющую не двигает и не убирает — он показывает, куда она встанет,
    /// и просит. Пока этот обработчик не выставит <c>Handled</c>, линия остаётся на месте.
    /// </remarks>
    private void Editor_OnGuideChangeRequested(object? sender, SurfaceGuideChangeRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var guides = viewModel.Guides;
        switch (e.Kind)
        {
            case SurfaceGuideChangeKind.Add:
                guides.Add(e.Guide);
                break;

            case SurfaceGuideChangeKind.Move:
                var index = e.Original is { } original ? guides.IndexOf(original) : -1;
                if (index < 0)
                    return;

                guides[index] = e.Guide;
                break;

            case SurfaceGuideChangeKind.Remove:
                if (!guides.Remove(e.Guide))
                    return;

                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void Editor_OnReorderRequested(object? sender, UiDesignerReorderRequestedEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.Target.GetVisualParent() is not Panel panel)
            return;

        // Индексы сняты редактором до вызова. Сверяем их с деревом, прежде чем
        // писать: обработчик — обычный код приложения, и полагаться на то, что
        // между запросом и правкой никто ничего не поменял, он не должен.
        if (panel.Children.IndexOf(e.Target) != e.OldIndex ||
            e.NewIndex < 0 || e.NewIndex >= panel.Children.Count)
        {
            return;
        }

        panel.Children.Move(e.OldIndex, e.NewIndex);

        // Правку выполнили здесь — значит, здесь же её и записываем. В поток
        // EditCompleted она не попадает: редактор структурой не распоряжается.
        _history?.Push(new ReorderChange(panel, e.OldIndex, e.NewIndex));
        e.Handled = true;

        // Дерево изменили здесь — здесь же и сообщаем панелям: перестановка и удаление
        // не проходят через EditCompleted, и узнать о них им больше неоткуда.
        RefreshPanels();
    }

    /// <summary>
    /// Сообщает панелям о правке дерева, сделанной хостом.
    /// </summary>
    /// <remarks>
    /// Обе панели читают редактор запросом, а он о чужих правках дерева не знает
    /// (ADR 0001): кто изменил дерево, тот и рассказывает. Точка одна — иначе следующая
    /// панель снова окажется забытой, как оказалась панель свойств.
    /// </remarks>
    private void RefreshPanels()
    {
        this.FindControl<GroupsPanel>("Groups")?.Refresh();
        this.FindControl<PropertiesPanel>("Properties")?.Refresh();
    }

    private void Undo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _history?.Undo();

    private void Redo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _history?.Redo();

    private void CenterActiveItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveItem == null)
            return;

        if (this.FindControl<ArxisStudio.Surface.UiDesigner.UiDesignerView>("Editor") is not { } editor)
            return;

        if (editor.ContainerFromItem(viewModel.ActiveItem) is UiDesignerItem container)
            editor.CenterOnItem(container);
    }

    private void FitActiveItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveItem == null)
            return;

        if (this.FindControl<ArxisStudio.Surface.UiDesigner.UiDesignerView>("Editor") is not { } editor)
            return;

        if (editor.ContainerFromItem(viewModel.ActiveItem) is UiDesignerItem container)
            editor.FitToView(container);
    }

    private void CenterSelection_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<ArxisStudio.Surface.UiDesigner.UiDesignerView>("Editor") is { } editor)
            editor.CenterOnSelection();
    }

    private void FitSelection_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<ArxisStudio.Surface.UiDesigner.UiDesignerView>("Editor") is { } editor)
            editor.FitSelectionToView();
    }
}
