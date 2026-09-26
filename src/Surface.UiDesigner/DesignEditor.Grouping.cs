using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Utilities;

namespace ArxisStudio.Surface.UiDesigner;

// Design-time группы: пометка на контролах, а не узел дерева.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Идентификатор свойства хранилища групп.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, IDesignGroupStore> GroupStoreProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IDesignGroupStore>(
            nameof(GroupStore),
            o => o.GroupStore,
            (o, v) => o.GroupStore = v);

    private IDesignGroupStore _groupStore = DesignGroupAttachedStore.Default;

    /// <summary>
    /// Получает или задает хранилище принадлежности контролов группам.
    /// </summary>
    /// <remarks>
    /// Редактор владеет смыслом группы, а местом хранения — тот, кто владеет документом
    /// (см. <see cref="IDesignGroupStore"/> и ADR 0002). Умолчание —
    /// <see cref="DesignGroupAttachedStore.Default"/>, то есть пометка на самом контроле.
    /// <para>
    /// Замена хранилища меняет состав групп, поэтому оверлей пересобирается сразу: кластеры
    /// считаются по пометке, и рамка обязана описывать то, что говорит новое хранилище.
    /// </para>
    /// <para>
    /// <see langword="null"/> означает библиотечное хранилище, а не «оставить как есть»: тот же
    /// приём, что у <c>SnapStep = NaN</c> и у курсоров жестов.
    /// </para>
    /// </remarks>
    public IDesignGroupStore GroupStore
    {
        get => _groupStore;
        set
        {
            var store = value ?? DesignGroupAttachedStore.Default;
            if (ReferenceEquals(store, _groupStore))
                return;

            DetachGroupStore(_groupStore);
            SetAndRaise(GroupStoreProperty, ref _groupStore, store);
            AttachGroupStore(store);
            UpdateSelectionOverlayState();
        }
    }

    /// <summary>
    /// Возвращает путь группы контрола по действующему хранилищу.
    /// </summary>
    /// <remarks>
    /// Единственная точка чтения пометки — пара к шву записи <see cref="SetDesignGroup"/>.
    /// Прежде на её месте стояло два десятка обращений к attached-свойству, и разойтись чтению
    /// с записью было нечем ровно потому, что место хранения было одно на всех; со сменным
    /// хранилищем это перестало быть правдой.
    /// </remarks>
    internal string? GetGroupOf(Control target) => _groupStore.GetGroup(target);

    /// <summary>
    /// Слабое событие изменения состава групп.
    /// </summary>
    /// <remarks>
    /// Хранилище хост вправе раздать нескольким редакторам — как и набор жестов, — а обычная
    /// подписка укладывает делегат в само хранилище, то есть хранилище начинает держать
    /// редактор и не отпускает ни одного. Причина та же и лечение то же, что у
    /// <c>InputGestureBridge</c>: событие держит подписчика слабо, мост держит редактор сильно,
    /// а редактор владеет мостом.
    /// </remarks>
    private static readonly WeakEvent<IDesignGroupStore, EventArgs> GroupsChangedWeakEvent =
        WeakEvent.Register<IDesignGroupStore>(
            static (store, handler) => store.GroupsChanged += handler,
            static (store, handler) => store.GroupsChanged -= handler);

    /// <summary>
    /// Ретранслятор события хранилища в пересборку оверлея.
    /// </summary>
    private sealed class GroupStoreBridge : IWeakEventSubscriber<EventArgs>
    {
        private readonly DesignEditor _editor;

        public GroupStoreBridge(DesignEditor editor) => _editor = editor;

        public void OnEvent(object? sender, WeakEvent ev, EventArgs e) => _editor.OnGroupsChanged();
    }

    private void AttachGroupStore(IDesignGroupStore store) =>
        GroupsChangedWeakEvent.Subscribe(store, _groupStoreBridge);

    private void DetachGroupStore(IDesignGroupStore store) =>
        GroupsChangedWeakEvent.Unsubscribe(store, _groupStoreBridge);

    /// <summary>
    /// Отвечает на чужую правку групп.
    /// </summary>
    /// <remarks>
    /// Пересобрать оверлей достаточно: он и закрывает вход в исчезнувшую группу, и заново
    /// считает кластеры. Свои правки сюда не приходят — редактор пишет через шов и знает о них.
    /// </remarks>
    private void OnGroupsChanged() => UpdateSelectionOverlayState();

    /// <summary>
    /// Путь группы, внутрь которой вошли двойным кликом.
    /// </summary>
    /// <remarks>
    /// Пока группа «открыта», клик выбирает её участника поодиночке, а не всю группу.
    /// Состояние снимается само, как только выбор уходит за её пределы: держать его до
    /// явного выхода значило бы завести режим, из которого пользователь не знает, как выйти.
    /// <para>
    /// Это путь, а не идентификатор: вложенность описывается уровнями, и «внутри
    /// <c>group-2/group-1</c>» — не то же самое, что «внутри <c>group-1</c>» где-то ещё.
    /// </para>
    /// </remarks>
    private string? _enteredGroupPath;

    /// <summary>
    /// Шов записи принадлежности к группе.
    /// </summary>
    /// <remarks>
    /// Третий шов рядом с <c>SetDesignPosition</c>/<c>SetDesignSize</c> и
    /// <c>SetDesignZIndex</c>, и заведён по той же причине: единственная точка записи —
    /// единственное место, где изменение попадает в контракт.
    /// </remarks>
    private void SetDesignGroup(Control target, string? id)
    {
        RecordEdit(_groupFacet, target, id);

        _groupStore.SetGroup(target, id);
    }

    /// <summary>
    /// Задает принадлежность к группе, не создавая новой единицы редактирования.
    /// </summary>
    /// <param name="target">Контрол.</param>
    /// <param name="id">Путь группы или <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Пара к <see cref="SurfaceView.ApplyGeometry"/> и <see cref="SurfaceView.ApplyOrder"/>: этим методом отмена
    /// и повтор применяют <see cref="DesignGroupChange"/>, не дописывая стек.
    /// </remarks>
    public void ApplyGroup(Control target, string? id)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var previous = _suppressEditRecording;
        _suppressEditRecording = true;
        try
        {
            SetDesignGroup(target, id);
        }
        finally
        {
            _suppressEditRecording = previous;
        }

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Объединяет выбранное в одну группу.
    /// </summary>
    /// <returns><see langword="true"/>, если группа создана.</returns>
    /// <remarks>
    /// Считаются не target'ы, а <b>кластеры</b>: выбранная целиком группа — это один
    /// кластер, и группировать её саму с собой нечего. Требуется не меньше двух кластеров
    /// внутри одной формы.
    /// <para>
    /// Группа-участник <b>вкладывается</b>, а не растворяется: её путь сохраняется хвостом,
    /// а новый уровень встаёт над общим родителем всех кластеров. Плоская модель на этом
    /// месте переписывала участникам пометку целиком, и вложенная группа исчезала.
    /// </para>
    /// <para>
    /// Группа поперёк форм не собирается намеренно: выделение двухслойно, и такая группа
    /// разошлась бы с индексным слоем — рамка обещала бы одно, а жест применялся к другому.
    /// </para>
    /// </remarks>
    public bool GroupSelection()
    {
        if (!TryCollectClusters(out var host, out var clusters) || clusters.Count < 2)
            return false;

        string? parent = null;
        var first = true;
        foreach (var cluster in clusters)
        {
            var clusterParent = ParentOf(cluster);
            parent = first ? clusterParent : DesignGroupPath.CommonPrefix(parent, clusterParent);
            first = false;
        }

        var path = DesignGroupPath.Append(parent, NextGroupId(host!, parent));
        var members = new List<Control>();

        BeginEdit(DesignEditKind.Group);
        foreach (var cluster in clusters)
        {
            foreach (var member in cluster.Members)
            {
                // Хвост пути сохраняется только у кластера-группы — ради этого вложенность
                // и заводилась. Одиночный контрол группой не является: он входит в новую
                // напрямую, иначе уровень, из которого его вытащили, уезжал бы с ним и
                // превращался в фантомную группу с тем же именем, что и настоящая.
                var next = cluster.IsGroup
                    ? DesignGroupPath.Rebase(GetGroupOf(member), parent, path)
                    : path;

                SetDesignGroup(member, next);
                AddDistinct(members, member);
            }
        }

        CommitEdit();

        // Открытый уровень задаёт SyncEnteredGroup по форме выделения: выбран ровно состав
        // новой группы, значит смотреть на неё надо снаружи.
        SelectGroupMembers(members);
        return true;
    }

    /// <summary>
    /// Распускает выбранные группы.
    /// </summary>
    /// <returns><see langword="true"/>, если хотя бы одна группа была распущена.</returns>
    /// <remarks>
    /// Снимается <b>один внешний уровень</b>: вложенные группы переживают роспуск и
    /// поднимаются на уровень выше. Дробить их заодно значило бы разрушить структуру,
    /// которую пользователь собирал отдельными действиями.
    /// </remarks>
    public bool UngroupSelection()
    {
        if (!TryCollectClusters(out var host, out var clusters))
            return false;

        var groups = clusters.Where(cluster => cluster.IsGroup).ToList();
        if (groups.Count == 0)
            return false;

        BeginEdit(DesignEditKind.Group);
        foreach (var cluster in groups)
        {
            var parent = DesignGroupPath.Parent(cluster.GroupPath);
            foreach (var member in EnumerateGroupMembers(host!, cluster.GroupPath!))
                SetDesignGroup(member, DesignGroupPath.Rebase(GetGroupOf(member), cluster.GroupPath, parent));
        }

        CommitEdit();

        _enteredGroupPath = null;
        UpdateSelectionOverlayState();
        return true;
    }

    /// <summary>
    /// Переименовывает уровень группы.
    /// </summary>
    /// <param name="container">Форма, которой принадлежит группа.</param>
    /// <param name="path">Путь группы.</param>
    /// <param name="newId">Новый идентификатор уровня — сегмент, а не путь.</param>
    /// <returns><see langword="true"/>, если группа переименована.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если любой из аргументов равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Переименование — это смена пометки у всех участников, поэтому идёт через тот же
    /// шов, что и группировка, и одной единицей редактирования: иначе оно не попало бы
    /// в <see cref="SurfaceView.EditCompleted"/> и отмена вернула бы всё, кроме имени группы.
    /// <para>
    /// Меняется <b>один сегмент</b>: переезд в другого родителя — это перемещение группы,
    /// отдельное действие. Отклоняется, если имя пустое или содержит разделитель, такой
    /// группы в форме нет, либо среди её братьев идентификатор уже занят — последнее
    /// означало бы слияние двух групп, а слияние обязано быть отдельным действием.
    /// </para>
    /// </remarks>
    public bool RenameGroup(DesignEditorItem container, string path, string newId)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        if (path == null)
            throw new ArgumentNullException(nameof(path));

        if (newId == null)
            throw new ArgumentNullException(nameof(newId));

        // Обрезка стоит на шве, а не в панели: правила имени должны жить в одном месте,
        // иначе « toolbar » и «toolbar» станут двумя внешне неотличимыми группами.
        newId = newId.Trim();
        if (!DesignGroupPath.IsValidSegment(newId))
            return false;

        var members = EnumerateGroupMembers(container, path).ToList();
        if (members.Count == 0)
            return false;

        var renamed = DesignGroupPath.Append(DesignGroupPath.Parent(path), newId);
        if (string.Equals(renamed, path, StringComparison.Ordinal) || IsGroupPathTaken(container, renamed!))
            return false;

        BeginEdit(DesignEditKind.Group);
        foreach (var member in members)
            SetDesignGroup(member, DesignGroupPath.Rebase(GetGroupOf(member), path, renamed));

        CommitEdit();

        // Вход в группу держится путём и переезжает вместе с ним: иначе группа осталась бы
        // открытой по имени, которого больше нет.
        if (DesignGroupPath.IsInside(_enteredGroupPath, path))
            _enteredGroupPath = DesignGroupPath.Rebase(_enteredGroupPath, path, renamed);

        UpdateSelectionOverlayState();
        return true;
    }

    /// <summary>
    /// Возвращает группы формы вместе с их составом.
    /// </summary>
    /// <param name="container">Форма, группы которой перечисляются.</param>
    /// <returns>Группы верхнего уровня; вложенные лежат в <see cref="DesignGroupInfo.Groups"/>.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="container"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Состав считается по дереву в момент вызова. Кэшировать его редактору нечем:
    /// деревом владеет хост, пометку он вправе поставить в разметке или через
    /// <see cref="DesignGroup.SetId"/>, и узнать об этом редактору неоткуда —
    /// сохранённый снимок молча устарел бы. По той же причине нет и события об изменении
    /// групп: о своих правках редактор сообщает через <see cref="SurfaceView.EditCompleted"/>, а о
    /// чужих сообщить не может.
    /// <para>
    /// Всё дерево снимается за один обход: два раздельных запроса дали бы уровни, снятые
    /// в разные моменты.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DesignGroupInfo> GetGroups(DesignEditorItem container)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        var nodes = new Dictionary<string, GroupNodeBuilder>(StringComparer.Ordinal);
        var roots = new List<GroupNodeBuilder>();

        foreach (var candidate in EnumerateGroupCandidates(container))
        {
            if (GetGroupOf(candidate) is not { } path)
                continue;

            EnsureNode(path, nodes, roots).Members.Add(candidate);
        }

        var result = new List<DesignGroupInfo>(roots.Count);
        foreach (var root in roots)
            result.Add(root.Build(container));

        return result;
    }

    /// <summary>
    /// Возвращает участников группы, включая лежащих во вложенных группах.
    /// </summary>
    /// <param name="container">Форма, которой принадлежит группа.</param>
    /// <param name="path">Путь группы.</param>
    /// <returns>Контролы в порядке обхода разметки; пустой список, если такой группы нет.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если любой из аргументов равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Неизвестный путь — это ответ, а не ошибка: состав меняется под хостом, и группа
    /// могла быть распущена между двумя его запросами.
    /// </remarks>
    public IReadOnlyList<Control> GetGroupMembers(DesignEditorItem container, string path)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        if (path == null)
            throw new ArgumentNullException(nameof(path));

        return EnumerateGroupMembers(container, path).ToList();
    }

    /// <summary>
    /// Возвращает признак того, что выделение можно объединить в группу.
    /// </summary>
    public bool CanGroupSelection() => TryCollectClusters(out _, out var clusters) && clusters.Count >= 2;

    /// <summary>
    /// Возвращает признак того, что в выделении есть что распускать.
    /// </summary>
    public bool CanUngroupSelection() =>
        TryCollectClusters(out _, out var clusters) && clusters.Any(cluster => cluster.IsGroup);

    /// <summary>
    /// Разбивает выделение на кластеры внутри одной формы.
    /// </summary>
    /// <remarks>
    /// Точка одна на всех потребителей — оверлей, группировку, роспуск: разойдясь, они
    /// снова показали бы одно, а применили другое.
    /// </remarks>
    private bool TryCollectClusters(out DesignEditorItem? host, out IReadOnlyList<SelectionCluster> clusters)
    {
        host = null;
        clusters = Array.Empty<SelectionCluster>();

        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var controls = new List<Control>(targets.Count);
        foreach (var selected in targets)
        {
            var target = selected.Target;

            // Контейнер формы группировать нечем: он и так рисуется одной рамкой,
            // а его принадлежность к форме задаёт ItemsSource, а не пометка.
            if (target is DesignEditorItem)
                return false;

            var owner = FindDesignHost(target);
            if (owner == null)
                return false;

            if (host == null)
                host = owner;
            else if (!ReferenceEquals(host, owner))
                return false;

            controls.Add(target);
        }

        if (host == null)
            return false;

        clusters = BuildClusters(host, controls);
        return true;
    }

    /// <summary>
    /// Собирает кластеры по видимому сейчас уровню вложенности.
    /// </summary>
    internal IReadOnlyList<SelectionCluster> BuildClusters(DesignEditorItem host, IReadOnlyList<Control> targets)
    {
        var selectedByPath = new Dictionary<string, List<Control>>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            if (ClusterPathOf(target) is not { } path)
                continue;

            if (!selectedByPath.TryGetValue(path, out var bucket))
            {
                bucket = new List<Control>();
                selectedByPath[path] = bucket;
            }

            bucket.Add(target);
        }

        // Группа становится кластером, только когда выбраны все её участники.
        var counts = CountGroupMembers(host);
        var whole = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in selectedByPath)
        {
            if (counts.TryGetValue(pair.Key, out var total) && total == pair.Value.Count)
                whole.Add(pair.Key);
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var clusters = new List<SelectionCluster>();

        foreach (var target in targets)
        {
            var path = ClusterPathOf(target);
            if (path != null && whole.Contains(path))
            {
                if (emitted.Add(path))
                    clusters.Add(SelectionCluster.Group(host, path, selectedByPath[path]));

                continue;
            }

            clusters.Add(SelectionCluster.Single(host, target));
        }

        return clusters;
    }

    /// <summary>Путь кластера, в который попадает контрол при текущем входе в группу.</summary>
    private string? ClusterPathOf(Control target) =>
        DesignGroupPath.ClusterOf(GetGroupOf(target), _enteredGroupPath);

    /// <summary>Путь группы, внутри которой лежит кластер.</summary>
    private string? ParentOf(SelectionCluster cluster) =>
        cluster.IsGroup
            ? DesignGroupPath.Parent(cluster.GroupPath)
            : GetGroupOf(cluster.Primary);

    private static void AddDistinct(List<Control> members, Control target)
    {
        if (!members.Contains(target))
            members.Add(target);
    }

    /// <summary>
    /// Считает состав каждой группы формы одним обходом.
    /// </summary>
    /// <remarks>
    /// Полноту кластера и совпадение выделения с группой спрашивают на каждой пересборке
    /// оверлея, то есть на каждом кадре жеста. Обход дерева стоит по размеру формы, и
    /// делать его по разу на кластер и на уровень пути значило вернуть ту самую
    /// зависимость от размера макета, из-за которой у рамки выделения появился снимок
    /// контейнеров.
    /// <para>
    /// Контрол считается участником каждого своего предка, поэтому счётчик получают все
    /// префиксы его пути.
    /// </para>
    /// </remarks>
    private Dictionary<string, int> CountGroupMembers(DesignEditorItem host)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var candidate in EnumerateGroupCandidates(host))
        {
            if (GetGroupOf(candidate) is not { } path)
                continue;

            var segments = DesignGroupPath.Split(path);
            for (var depth = 1; depth <= segments.Length; depth++)
            {
                if (DesignGroupPath.Combine(segments.Take(depth)) is not { } key)
                    continue;

                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Перечисляет участников группы, включая вложенные уровни.
    /// </summary>
    /// <remarks>
    /// Кандидатов даёт тот же обход, что и выделение: группа не может состоять
    /// из того, что редактор не считает отдельным элементом.
    /// </remarks>
    internal IEnumerable<Control> EnumerateGroupMembers(DesignEditorItem host, string path)
    {
        foreach (var candidate in EnumerateGroupCandidates(host))
        {
            if (DesignGroupPath.IsInside(GetGroupOf(candidate), path))
                yield return candidate;
        }
    }

    /// <summary>
    /// Перечисляет то, что вообще может оказаться участником группы.
    /// </summary>
    /// <remarks>
    /// Отбор здесь один на всех потребителей — раскрытие по клику, роспуск, публичное
    /// чтение, — иначе они разошлись бы в понимании состава. Без <c>IsSelectableTarget</c>
    /// участником становился бы контрол, помеченный хостом, но не имеющий designer-метаданных:
    /// клик по соседу раскрывал бы выделение на то, что указатель выбрать не может.
    /// </remarks>
    private static IEnumerable<Control> EnumerateGroupCandidates(DesignEditorItem host)
    {
        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (IsSelectableTarget(candidate, host))
                yield return candidate;
        }
    }

    /// <summary>
    /// Признак того, что путь в форме уже занят — им самим или чем-то внутри него.
    /// </summary>
    /// <remarks>
    /// Обход <b>не фильтруется</b>, в отличие от состава группы: пометка, которую редактор
    /// не считает элементом, занимает путь наравне с видимой, и переезд на него слил бы
    /// группы при первом же сохранении.
    /// </remarks>
    private bool IsGroupPathTaken(DesignEditorItem host, string path)
    {
        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (DesignGroupPath.IsInside(GetGroupOf(candidate), path))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Подбирает идентификатор уровня, свободный среди братьев под этим родителем.
    /// </summary>
    /// <remarks>
    /// Обход не фильтруется по той же причине, что и у <see cref="IsGroupPathTaken"/>.
    /// Свобода проверяется <b>внутри родителя</b>: одинаковые имена на разных ветках
    /// не сталкиваются, потому что личность группы — это её путь целиком.
    /// </remarks>
    private string NextGroupId(DesignEditorItem host, string? parent)
    {
        var depth = DesignGroupPath.Depth(parent);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            var path = GetGroupOf(candidate);
            if (!DesignGroupPath.IsInside(path, parent))
                continue;

            var segments = DesignGroupPath.Split(path);
            if (segments.Length > depth)
                used.Add(segments[depth]);
        }

        for (var i = 1; ; i++)
        {
            var id = "group-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(id))
                return id;
        }
    }

    /// <summary>
    /// Раскрывает target до кластера, в который он попадает.
    /// </summary>
    /// <remarks>
    /// Возвращает сам target, если он не сгруппирован или лежит прямо в открытой группе.
    /// Точка одна: раскрытие обязано совпадать у указателя и у оверлея, иначе рамка
    /// снова обещала бы не то, что применится.
    /// </remarks>
    private IReadOnlyList<Control> ExpandToGroup(DesignEditorItem host, Control target)
    {
        if (ClusterPathOf(target) is not { } path)
            return new[] { target };

        var members = EnumerateGroupMembers(host, path).ToList();
        return members.Count > 0 ? members : new List<Control> { target };
    }

    /// <summary>
    /// Опускает вход в группу на уровень и закрывает его кликом мимо.
    /// </summary>
    /// <remarks>
    /// Двойной клик спускается ровно на один уровень: первый выбирает вложенную группу
    /// целиком, следующий входит уже в неё. Провалиться сразу до контрола значило бы
    /// сделать промежуточные уровни невыбираемыми указателем.
    /// </remarks>
    private void UpdateEnteredGroup(Control target, int clickCount)
    {
        var path = GetGroupOf(target);

        if (clickCount >= 2)
        {
            if (DesignGroupPath.ClusterOf(path, _enteredGroupPath) is { } next)
                _enteredGroupPath = next;

            return;
        }

        if (!DesignGroupPath.IsInside(path, _enteredGroupPath))
            _enteredGroupPath = null;
    }

    /// <summary>
    /// Приводит открытый уровень в соответствие с выделением.
    /// </summary>
    /// <remarks>
    /// Точка одна и стоит на пересборке оверлея: делать это в каждом месте, где меняется
    /// выбор, значило бы перечислить их все — а их два десятка, и клик по пустому холсту
    /// в этом списке уже терялся.
    /// <para>
    /// Правил два. Выбран ровно состав какой-то группы — значит смотрим на неё
    /// <b>снаружи</b>, и открытым остаётся её родитель: иначе группа, выбранная не
    /// указателем, а через <see cref="SurfaceView.SelectDesignTarget"/>, рисовалась бы участниками
    /// поодиночке. Именно так и ломался выбор из панели групп: вход, открытый двойным
    /// кликом, переживал его и разбивал выделение на отдельные контролы.
    /// </para>
    /// <para>
    /// Иначе вход закрывается, когда из его группы не осталось ничего выбранного.
    /// </para>
    /// </remarks>
    private void SyncEnteredGroup()
    {
        if (TryResolveSelectedGroup(out var selectedGroup))
        {
            _enteredGroupPath = DesignGroupPath.Parent(selectedGroup);
            return;
        }

        if (_enteredGroupPath == null)
            return;

        foreach (var target in _selectedTargets)
        {
            if (DesignGroupPath.IsInside(GetGroupOf(target), _enteredGroupPath))
                return;
        }

        _enteredGroupPath = null;
    }

    /// <summary>
    /// Определяет группу, состав которой совпадает с выделением.
    /// </summary>
    /// <remarks>
    /// Берётся <b>самая внешняя</b> подходящая: если состав вложенной совпал с составом
    /// внешней, показать надо внешнюю — это она целиком и выбрана.
    /// </remarks>
    private bool TryResolveSelectedGroup(out string? path)
    {
        path = null;

        if (_selectedTargets.Count == 0)
            return false;

        DesignEditorItem? host = null;
        string? prefix = null;
        var first = true;

        foreach (var target in _selectedTargets)
        {
            if (GetGroupOf(target) is not { } current)
                return false;

            if (FindDesignHost(target) is not { } owner)
                return false;

            if (host == null)
                host = owner;
            else if (!ReferenceEquals(host, owner))
                return false;

            prefix = first ? current : DesignGroupPath.CommonPrefix(prefix, current);
            first = false;
        }

        if (host == null || prefix == null)
            return false;

        var counts = CountGroupMembers(host);
        var segments = DesignGroupPath.Split(prefix);

        for (var depth = 1; depth <= segments.Length; depth++)
        {
            var candidate = DesignGroupPath.Combine(segments.Take(depth));
            if (candidate == null)
                continue;

            // Все выбранные лежат внутри кандидата — он префикс их общего префикса, —
            // поэтому равенства количеств достаточно: состав уникален.
            if (!counts.TryGetValue(candidate, out var total) || total != _selectedTargets.Count)
                continue;

            path = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Признак того, что контрол лежит внутри открытой группы.
    /// </summary>
    private bool IsInsideEnteredGroup(Control target) =>
        _enteredGroupPath != null
        && DesignGroupPath.IsInside(GetGroupOf(target), _enteredGroupPath);

    private void SelectGroupMembers(IReadOnlyList<Control> members)
    {
        if (members.Count == 0)
            return;

        ApplySelection(members, SelectionIntent.Replace);
    }

    private static GroupNodeBuilder EnsureNode(
        string path,
        Dictionary<string, GroupNodeBuilder> nodes,
        List<GroupNodeBuilder> roots)
    {
        if (nodes.TryGetValue(path, out var existing))
            return existing;

        var node = new GroupNodeBuilder(path);
        nodes[path] = node;

        // Промежуточный уровень заводится вместе с потомком: группа без собственных
        // контролов — обычное дело, она держит только вложенные.
        if (DesignGroupPath.Parent(path) is { } parent)
            EnsureNode(parent, nodes, roots).Children.Add(node);
        else
            roots.Add(node);

        return node;
    }

    /// <summary>Промежуточный узел дерева групп: у публичного снимка состав неизменяем.</summary>
    private sealed class GroupNodeBuilder
    {
        public GroupNodeBuilder(string path) => Path = path;

        public string Path { get; }

        public List<Control> Members { get; } = new();

        public List<GroupNodeBuilder> Children { get; } = new();

        public DesignGroupInfo Build(DesignEditorItem container)
        {
            var groups = new List<DesignGroupInfo>(Children.Count);
            foreach (var child in Children)
                groups.Add(child.Build(container));

            return new DesignGroupInfo(container, Path, Members, groups);
        }
    }
}
