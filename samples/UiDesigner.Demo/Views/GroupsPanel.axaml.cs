using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ArxisStudio;
using UiDesigner.Demo.ViewModels;
using Editor = ArxisStudio.Surface.UiDesigner.UiDesignerView;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Панель групп: формы, их группы и участники групп.
/// </summary>
/// <remarks>
/// Панель не знает ни одного внутреннего типа библиотеки: состав читается
/// <see cref="Editor.GetGroups"/>, подписи строятся публичным
/// <see cref="SurfaceSelectionTarget"/>, выбор задаётся
/// <see cref="Editor.SelectTarget"/>, а группировка — <see cref="Editor.GroupSelection"/>
/// и <see cref="Editor.UngroupSelection"/>.
/// <para>
/// Показываются <b>группы</b>, а не всё дерево контролов, и это не упрощение: публично
/// редактор отвечает на вопрос про группы, а перечислить всё, что он считает элементом,
/// снаружи нельзя. Панель показывает ровно то, на что API отвечает.
/// </para>
/// <para>
/// Чтение групп — запрос, а не снимок: редактор не владеет деревом и не может сообщить
/// о пометке, которую поставил не он. Поэтому панель пересобирается на событиях самого
/// редактора (<see cref="Editor.SurfaceSelectionChanged"/> и <see cref="Editor.EditCompleted"/>),
/// а на всё остальное есть кнопка обновления — это честная цена pull-модели, а не недоделка.
/// </para>
/// </remarks>
public partial class GroupsPanel : UserControl
{
    /// <summary>Идентификатор свойства редактора.</summary>
    public static readonly StyledProperty<Editor?> EditorProperty =
        AvaloniaProperty.Register<GroupsPanel, Editor?>(nameof(Editor));

    /// <summary>Идентификатор свойства доступности группировки.</summary>
    public static readonly StyledProperty<bool> CanGroupProperty =
        AvaloniaProperty.Register<GroupsPanel, bool>(nameof(CanGroup));

    /// <summary>Идентификатор свойства доступности роспуска.</summary>
    public static readonly StyledProperty<bool> CanUngroupProperty =
        AvaloniaProperty.Register<GroupsPanel, bool>(nameof(CanUngroup));

    /// <summary>Свёрнутые узлы; переживает пересборку списка.</summary>
    private readonly HashSet<string> _collapsed = new(System.StringComparer.Ordinal);

    private Editor? _attached;
    private bool _builtOnce;

    /// <summary>Строка, чьё имя сейчас правится; переживает пересборку списка.</summary>
    private string? _editingKey;

    /// <summary>Инициализирует панель.</summary>
    public GroupsPanel()
    {
        // Панель — сама себе модель: строки она строит из редактора, а данных окна
        // ей не нужно. Привязка Editor приходит снаружи по имени элемента и от
        // DataContext не зависит.
        DataContext = this;
        InitializeComponent();
    }

    /// <summary>Получает или задает редактор, чьи группы показывает панель.</summary>
    public Editor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Получает признак того, что выделение можно сгруппировать.</summary>
    public bool CanGroup
    {
        get => GetValue(CanGroupProperty);
        private set => SetValue(CanGroupProperty, value);
    }

    /// <summary>Получает признак того, что в выделении есть что распустить.</summary>
    public bool CanUngroup
    {
        get => GetValue(CanUngroupProperty);
        private set => SetValue(CanUngroupProperty, value);
    }

    /// <summary>Строки панели в порядке показа.</summary>
    public ObservableCollection<GroupNode> Nodes { get; } = new();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty)
            Attach(change.GetNewValue<Editor?>());
    }

    private void Attach(Editor? editor)
    {
        if (_attached != null)
        {
            _attached.SurfaceSelectionChanged -= OnSurfaceSelectionChanged;
            _attached.EditCompleted -= OnEditCompleted;
            _attached.LayoutUpdated -= OnEditorLayoutUpdated;
        }

        _attached = editor;
        _builtOnce = false;

        if (_attached == null)
        {
            Nodes.Clear();
            return;
        }

        _attached.SurfaceSelectionChanged += OnSurfaceSelectionChanged;
        _attached.EditCompleted += OnEditCompleted;

        // Контейнеры существуют только после layout-прохода, поэтому первая сборка
        // ждёт его: до него ContainerFromIndex вернёт null и панель окажется пустой.
        _attached.LayoutUpdated += OnEditorLayoutUpdated;
    }

    private void OnEditorLayoutUpdated(object? sender, System.EventArgs e)
    {
        if (_builtOnce)
            return;

        _builtOnce = true;
        Rebuild();
    }

    private void OnSurfaceSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e) => Rebuild();

    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e) => Rebuild();

    /// <summary>
    /// Перечитывает группы у редактора.
    /// </summary>
    /// <remarks>
    /// Зовётся хостом после того, как он <b>сам</b> изменил дерево — переставил детей
    /// или удалил форму. Такие правки редактор не делает и не публикует
    /// (см. ADR 0001), поэтому и сообщить о них может только тот, кто их выполнил.
    /// Без этого панель остаётся с прежним составом: строки продолжают указывать на
    /// контролы, стоявшие на этих местах до перестановки.
    /// </remarks>
    public void Refresh() => Rebuild();

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) => Rebuild();

    private void Group_OnClick(object? sender, RoutedEventArgs e)
    {
        _attached?.GroupSelection();
        Rebuild();
    }

    private void Ungroup_OnClick(object? sender, RoutedEventArgs e)
    {
        _attached?.UngroupSelection();
        Rebuild();
    }

    /// <summary>
    /// Пересобирает список по текущему состоянию редактора.
    /// </summary>
    /// <remarks>
    /// Строки <b>сверяются по ключу</b>, а не создаются заново. Полная пересборка на
    /// каждое изменение выбора подменяла бы визуал под указателем — и двойной клик
    /// переставал быть двойным: второе нажатие приходило уже в другой элемент.
    /// </remarks>
    private void Rebuild()
    {
        if (_attached is not { } editor)
            return;

        Reconcile(BuildRows(editor));

        CanGroup = editor.CanGroupSelection();
        CanUngroup = editor.CanUngroupSelection();
    }

    /// <summary>
    /// Приводит текущий список к желаемому, сохраняя строки, описывающие то же самое.
    /// </summary>
    private void Reconcile(List<GroupNode> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < Nodes.Count && IsSameRow(Nodes[i], rows[i]))
            {
                var existing = Nodes[i];
                existing.IsSelected = rows[i].IsSelected;
                existing.IsExpanded = rows[i].IsExpanded;
                existing.MemberCount = rows[i].MemberCount;
                existing.IsEditing = rows[i].IsEditing;
                continue;
            }

            if (i < Nodes.Count)
                Nodes[i] = rows[i];
            else
                Nodes.Add(rows[i]);
        }

        while (Nodes.Count > rows.Count)
            Nodes.RemoveAt(Nodes.Count - 1);
    }

    /// <summary>
    /// Признак того, что старую строку можно оставить вместо новой.
    /// </summary>
    /// <remarks>
    /// Правило одно: <b>переиспользовать можно только строку, у которой совпадает всё,
    /// что сверка не переносит</b>. Она переносит лишь изменяемое состояние, поэтому
    /// контрол и форма обязаны совпасть по ссылке.
    /// <para>
    /// Одного ключа тут мало, и это стоило дефекта: ключ участника позиционный, а после
    /// перестановки детей в панели позиции меняются местами. Ключи при этом совпадали,
    /// строки оставались прежними — и первая строка выбирала контрол, ставший вторым.
    /// </para>
    /// </remarks>
    private static bool IsSameRow(GroupNode existing, GroupNode fresh) =>
        string.Equals(existing.Key, fresh.Key, System.StringComparison.Ordinal)
        && ReferenceEquals(existing.Target, fresh.Target)
        && ReferenceEquals(existing.Container, fresh.Container);

    private List<GroupNode> BuildRows(Editor editor)
    {
        var rows = new List<GroupNode>();

        var selected = new HashSet<Control>();
        foreach (var target in editor.SelectedTargets)
            selected.Add(target.Target);

        for (var index = 0; index < editor.ItemCount; index++)
        {
            if (editor.ContainerFromIndex(index) is not UiDesignerItem container)
                continue;

            var groups = editor.GetGroups(container);
            var formKey = "form:" + index;
            var formExpanded = !_collapsed.Contains(formKey);

            rows.Add(new GroupNode(GroupNodeKind.Form, formKey, TitleOf(editor, index, container), depth: 0)
            {
                Container = container,
                HasChildren = true,
                IsExpanded = formExpanded,
                IsSelected = selected.Contains(container)
            });

            if (!formExpanded)
                continue;

            if (groups.Count == 0)
            {
                rows.Add(new GroupNode(GroupNodeKind.Hint, formKey + ":empty", "групп нет", depth: 1));
                continue;
            }

            foreach (var group in groups)
                AddGroup(rows, container, group, formKey, selected, depth: 1);
        }

        return rows;
    }

    /// <summary>
    /// Выкладывает группу и всё, что в ней лежит.
    /// </summary>
    /// <remarks>
    /// Рекурсия по <see cref="SurfaceGroupInfo.Groups"/>: группа внутри группы — обычный
    /// узел, и панель показывает её тем же отступом, что и любую вложенность.
    /// Ключ строки — полный путь: одинаковые имена уровней на разных ветках не должны
    /// схлопываться при сверке.
    /// </remarks>
    private void AddGroup(
        List<GroupNode> rows,
        UiDesignerItem container,
        SurfaceGroupInfo group,
        string formKey,
        HashSet<Control> selected,
        int depth)
    {
        var key = formKey + ":" + group.Path;
        var expanded = !_collapsed.Contains(key);

        // Группа считается выбранной, когда выбран весь её состав, включая вложенные
        // уровни: рамка у неё одна, и половинчатая подсветка описывала бы состояние,
        // которого нет. Состав берётся из уже построенного дерева, а не запросом на
        // каждую строку: панель пересобирается на каждое изменение выбора, и запрос
        // означал бы полный обход формы на каждую группу при каждом клике.
        var members = new List<Control>();
        CollectMembers(group, members);
        var wholeGroupSelected = members.Count > 0;
        foreach (var member in members)
        {
            if (!selected.Contains(member))
            {
                wholeGroupSelected = false;
                break;
            }
        }

        var node = new GroupNode(GroupNodeKind.Group, key, group.Id, depth)
        {
            Container = container,
            GroupPath = group.Path,
            MemberCount = members.Count,
            HasChildren = true,
            IsExpanded = expanded,
            IsSelected = wholeGroupSelected
        };

        if (string.Equals(_editingKey, key, System.StringComparison.Ordinal))
        {
            node.EditText = group.Id;
            node.IsEditing = true;
        }

        rows.Add(node);

        if (!expanded)
            return;

        foreach (var nested in group.Groups)
            AddGroup(rows, container, nested, formKey, selected, depth + 1);

        for (var i = 0; i < group.Members.Count; i++)
        {
            var member = group.Members[i];

            // Ключ участника считается от его места в группе, а не от длины списка:
            // строка выше не должна перенумеровывать строки ниже, иначе сверка по ключу
            // перестала бы находить их на прежних местах.
            rows.Add(new GroupNode(GroupNodeKind.Member, key + ":" + i, TitleOf(container, member), depth + 1)
            {
                Container = container,
                Target = member,
                GroupPath = group.Path,
                IsSelected = selected.Contains(member)
            });
        }
    }

    /// <summary>Собирает весь состав группы по дереву, включая вложенные уровни.</summary>
    private static void CollectMembers(SurfaceGroupInfo group, List<Control> into)
    {
        into.AddRange(group.Members);

        foreach (var nested in group.Groups)
            CollectMembers(nested, into);
    }

    private static string TitleOf(UiDesignerItem container, Control member) =>
        new SurfaceSelectionTarget(container, member).DisplayName;

    private static string TitleOf(Editor editor, int index, UiDesignerItem container)
    {
        if (editor.Items[index] is SurfaceItemViewModel item)
            return item.Title;

        return new SurfaceSelectionTarget(container, container).DisplayName;
    }

    private void Row_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GroupNode node } || _attached is not { } editor)
            return;

        var point = e.GetCurrentPoint(sender as Visual);
        if (!point.Properties.IsLeftButtonPressed || node.IsEditing)
            return;

        // Двойной клик по имени группы правит его — как в любом дереве объектов.
        if (e.ClickCount == 2 && node.Kind == GroupNodeKind.Group)
        {
            BeginRename(node);
            e.Handled = true;
            return;
        }

        // Стрелка раскрытия занимает первые пиксели строки — клик по ней сворачивает,
        // а не выбирает: то же деление, что в любом дереве.
        if (node.HasChildren && point.Position.X <= node.Indent.Left + 14)
        {
            Toggle(node);
            e.Handled = true;
            return;
        }

        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (node.Kind)
        {
            case GroupNodeKind.Form when node.Container != null:
                editor.SelectTarget(node.Container, additive);
                break;

            case GroupNodeKind.Member when node.Target != null:
                editor.SelectTarget(node.Target, additive);
                break;

            case GroupNodeKind.Group when node.Container != null && node.GroupPath != null:
                SelectGroup(editor, node);
                break;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Выбирает группу целиком.
    /// </summary>
    /// <remarks>
    /// Раскрытие клика до всей группы — поведение указателя над холстом; публичный
    /// <see cref="Editor.SelectTarget"/> работает с одним target'ом, поэтому
    /// панель набирает участников сама: первый заменяет выбор, остальные добавляются.
    /// </remarks>
    private static void SelectGroup(Editor editor, GroupNode node)
    {
        var members = editor.GetGroupMembers(node.Container!, node.GroupPath!);
        for (var i = 0; i < members.Count; i++)
            editor.SelectTarget(members[i], additive: i > 0);
    }

    /// <summary>
    /// Начинает правку имени группы.
    /// </summary>
    /// <remarks>
    /// Ключ правки держится у панели, а не у строки: список пересобирается на каждое
    /// событие редактора, и состояние, лежащее в строке, не пережило бы первую же
    /// пересборку — тем же способом хранится и свёрнутость.
    /// </remarks>
    private void BeginRename(GroupNode node)
    {
        _editingKey = node.Key;
        node.EditText = node.Title;
        node.IsEditing = true;
    }

    private void EditBox_OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        box.Focus();
        box.SelectAll();
    }

    private void EditBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: GroupNode node })
            return;

        switch (e.Key)
        {
            case Key.Enter:
                CommitRename(node);
                e.Handled = true;
                break;

            case Key.Escape:
                CancelRename();
                e.Handled = true;
                break;
        }
    }

    private void EditBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GroupNode node } && node.IsEditing)
            CommitRename(node);
    }

    /// <summary>
    /// Применяет новое имя группы.
    /// </summary>
    /// <remarks>
    /// Отказ редактора — пустое имя, занятый идентификатор, исчезнувшая группа — панель
    /// не обсуждает: она просто возвращает прежнее имя. Своей проверки здесь нет намеренно,
    /// иначе правила существовали бы в двух местах и разошлись бы.
    /// </remarks>
    private void CommitRename(GroupNode node)
    {
        _editingKey = null;
        node.IsEditing = false;

        if (_attached is { } editor && node.Container != null && node.GroupPath != null)
            editor.RenameGroup(node.Container, node.GroupPath, node.EditText);

        Rebuild();
    }

    private void CancelRename()
    {
        _editingKey = null;
        Rebuild();
    }

    private void Toggle(GroupNode node)
    {
        if (!_collapsed.Add(node.Key))
            _collapsed.Remove(node.Key);

        Rebuild();
    }
}
