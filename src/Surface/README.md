# ArxisStudio.Surface

Ядро семейства: бесконечная поверхность для Avalonia 12. Viewport с панорамированием и зумом, фоновая
сетка с уровнями детализации, контейнеры элементов, двухуровневое выделение, жесты, клавиатурные
команды, контракт изменений и готовый стек отмены. Что лежит на холсте — форма, узел графа, фигура, —
ядро не знает: предметную логику добавляют [инструменты](../Surface.Editing/README.md),
[дизайнер интерфейса](../Surface.UiDesigner/README.md) и [редактор узлов](../Surface.Nodes/README.md).

| | |
| --- | --- |
| Пакет и сборка | `ArxisStudio.Surface`, `net8.0` |
| Пространство имён | `ArxisStudio.Surface` |
| Адрес разметки | `https://github.com/Arxis-Team/ArxisStudio.Surface` |
| Тема | `avares://ArxisStudio.Surface/Themes/SurfaceTheme.axaml` |
| Зависимости | Avalonia 12.1 |

## Подключение

```xml
<Application.Resources>
    <ResourceInclude Source="avares://ArxisStudio.Surface/Themes/SurfaceTheme.axaml" />
</Application.Resources>
```

Тема ядра рисует фон, сетку, контейнер и рамку выделения. Точки входа дизайнера интерфейса и редактора
узлов включают её сами: приложение подключает одну тему своего слоя.

## Поверхность и контейнеры

`SurfaceView : SelectingItemsControl` — поверхность; `SurfaceItem : ContentControl` — контейнер
элемента `ItemsSource`; `SurfacePanel` ставит контейнер в его `Location` (мировые координаты левого
верхнего угла) и публикует охват элементов в `SurfaceView.ItemsExtent`.

```xml
<surface:SurfaceView ItemsSource="{Binding Shapes}" SelectionMode="Multiple">
    <surface:SurfaceView.ItemContainerTheme>
        <ControlTheme TargetType="surface:SurfaceItem"
                      BasedOn="{StaticResource {x:Type surface:SurfaceItem}}">
            <Setter Property="Location" Value="{Binding Location}" />
        </ControlTheme>
    </surface:SurfaceView.ItemContainerTheme>
    <surface:SurfaceView.ItemTemplate>
        <DataTemplate x:DataType="vm:ShapeViewModel">
            <Border Width="{Binding Width}" Height="{Binding Height}" Background="{Binding Fill}" />
        </DataTemplate>
    </surface:SurfaceView.ItemTemplate>
</surface:SurfaceView>
```

Перетаскивание пишет `Location` локальным значением, и привязка из стиля ему уступает. Итог жеста
модель получает из `EditCompleted` (см. «Контракт изменений»), а начальное положение можно ставить и
кодом в `ContainerPrepared` — так делает демо редактора узлов.

`SurfaceItem.IsDraggable = false` запрещает перетаскивать контейнер. Маршрутизируемые события
контейнера: `DragStarted` (точка нажатия в координатах редактора — несмотря на имена
`HorizontalOffset`/`VerticalOffset`), `DragDelta` (**применённое** смещение кадра — после привязки, политик и
ограничений, а не движение указателя), `DragCompleted` (сумма применённых смещений, `Canceled`);
`ResizeStarted`/`ResizeDelta`/`ResizeCompleted` для изменения размера ручками.

Наследнику `SurfaceView` без своей темы нужен `protected override Type StyleKeyOverride =>
typeof(SurfaceView)`: Avalonia ищет `ControlTheme` по точному типу.

## Viewport и координаты

| Член | Что делает |
| --- | --- |
| `ViewportLocation` | мировая точка в левом верхнем углу видимой области |
| `ViewportZoom`, `MinZoom` (0,1), `MaxZoom` (5) | масштаб и его пределы |
| `ZoomAt(zoom, origin)` | масштаб вокруг точки экрана: точка холста под `origin` остаётся на месте |
| `CenterOn(Point)`, `CenterOn(Rect)`, `CenterOnItem(SurfaceItem)` | сдвиг viewport, масштаб не меняется |
| `FitToView(Rect)`, `FitToView(SurfaceItem)` | сдвиг и масштаб в пределах `MinZoom`/`MaxZoom`, с полем |
| `GetWorldPosition(Point)` | точка редактора → мир: `p / ViewportZoom + ViewportLocation` |
| `ItemsExtent` | рамка всех контейнеров в мировых координатах |
| `ViewportTransform`, `DpiScaledViewportTransform` | трансформации для слоёв шаблона; вторая со смещением, округлённым до физического пикселя |

Систем координат три, и путать их нельзя:

1. **координаты редактора** — `e.GetPosition(view)`, пиксели viewport'а;
2. **мировые** — после `GetWorldPosition`; в них считаются рамка выделения и попадание;
3. **координаты поверхности** — положение target'а относительно холста; в них живут
   `GeometryChange.OldBounds`/`NewBounds` и рамки выделения. У контейнера верхнего уровня они
   совпадают с мировыми.

## Сетка

`SurfaceGrid` — нижний слой шаблона, включается `ShowGrid` (по умолчанию `true`). Это рендерящийся
контрол, а не плиточная `DrawingBrush`: он рисует только видимые линии в экранных координатах.

- Толщина задаётся в пикселях устройства и не растёт с зумом.
- Уровень детализации скрывается, когда его экранный шаг меньше `MinCellSize`: на отдалении остаётся
  разреженная мажорная сетка.
- Шаг задаётся числом. Инструменты привязки берут его у сетки, если `SnapStep` равен `NaN`.

| Ключ | Назначение |
| --- | --- |
| `Surface.Grid.CellSize` | шаг, мировые единицы (20) |
| `Surface.Grid.MajorInterval` | период мажорных линий, в ячейках (5) |
| `Surface.Grid.LineThickness` | толщина, пиксели устройства |
| `Surface.Grid.MinCellSize` | порог скрытия уровня, пиксели экрана |
| `Surface.Grid.BackgroundBrush`, `Surface.Grid.LineBrush`, `Surface.Grid.MajorLineBrush` | кисти, свои для `Light` и `Dark` |

Для своего фона — `ShowGrid="False"` и `Background` на поверхности. `SurfaceGrid` работает и отдельно,
если связать его `ViewportLocation` и `ViewportZoom` с поверхностью.

## Выделение

Выделение двухуровневое.

- **Индексный слой** — модель `SelectingItemsControl`: `Selection`, `SelectedItems`, `SelectionMode`,
  `SurfaceItem.IsSelected`. Оперирует контейнерами.
- **Слой target'ов** — `SelectedTargets` (`IReadOnlyList<SurfaceSelectionTarget>`),
  `SelectedTargetsCount`, `PrimarySelectionTarget` (первый в списке). Target — контейнер целиком
  (`SurfaceSelectionScope.Container`) или вложенный контрол (`NestedTarget`), если его предлагает слой
  выше. У голого ядра target всегда контейнер.

`SurfaceSelectionTarget`: `Container`, `Target`, `Scope`, `Depth`, `DisplayName`, `GroupId`.

`SurfaceSelectionChanged` сообщает о смене набора target'ов или primary и только о ней: перетаскивание,
изменение размера и повторный клик по выбранному его не поднимают. Аргументы — `OldTargets`,
`NewTargets`, `Added`, `Removed`, `OldPrimary`, `NewPrimary`, `IsPrimaryChanged`. Наборы сравниваются по
паре `(Target, GroupId)`, поэтому пересборка обёрток его не поднимает, а `SelectedTargets` сохраняет
прежний экземпляр, пока набор не изменился. Унаследованное `SelectionChanged` — событие индексного
слоя, это другое.

```csharp
view.SurfaceSelectionChanged += (_, e) =>
{
    if (e.IsPrimaryChanged)
        inspector.Show(e.NewPrimary?.Target);
};
```

`SelectTarget(control, additive = false)` выбирает контрол так же, как клик по нему, и меняет оба слоя
согласованно. Возвращает `false`, если контрол нельзя выбрать. Это обратная дорога для хоста со своим
деревом: клик по строке дерева выбирает контрол на холсте. `SelectedIndex`, `SelectedItem` и
`Selection` пишут только индексный слой.

Рамка выделения: `IsSelecting`, `SelectedArea` (мировые координаты), `MarqueeScope` — контейнер,
в пределах которого идёт рамка, или `null`. Владелец рамки пересчитывается на каждом шаге протяжки по
её текущему прямоугольнику, режим (контейнеры или содержимое) фиксируется на нажатии.

## Жесты и их настройка

Жесты, модификаторы и числа вынесены в два объекта, которые задаются из разметки, стиля или привязки.

`SurfaceInputGestures` (`SurfaceView.InputGestures`):

| Свойство | По умолчанию | Смысл |
| --- | --- | --- |
| `PanButton`, `PanModifiers` | `Middle`, `None` | панорамирование |
| `MarqueeButton`, `MarqueeModifiers` | `Left`, `None` | рамка по пустому месту |
| `ZoomModifiers` | `None` | модификатор масштаба колесом |
| `ContainerInteractionModifiers` | `Control` | выбор и перетаскивание контейнера целиком |
| `AdditiveSelectionModifiers` | `Shift` | добавление к выделению |
| `ContainerEmptyAreaDrag` | `Marquee` | протяжка с пустого места контейнера: рамка или `MoveContainer` |
| `LargeNudgeModifiers` | `Shift` | крупный шаг смещения стрелками |
| `KeyboardResizeModifiers` | `Alt` | изменение размера стрелками; `None` выключает |
| `SnapBypassModifiers` | `Alt` | временный обход привязки |
| `UndoGestures`, `RedoGestures` | `null` | `null` — сочетания платформы; список заменяет их целиком |

`SurfaceInteractionOptions` (`SurfaceView.InteractionOptions`):

| Свойство | По умолчанию | Смысл |
| --- | --- | --- |
| `ZoomStep` | 1,1 | множитель на щелчок колеса |
| `IsPinchZoomEnabled` | `true` | масштаб щипком тачпада и пальцами |
| `IsAutoPanEnabled`, `AutoPanEdge`, `AutoPanSpeed` | `true`, 32, 900 | автопрокрутка у края: полоса и скорость в пикселях экрана |
| `DragStartThreshold` | 3 | порог начала протяжки, пиксели |
| `NudgeStep`, `LargeNudgeStep` | 1, 10 | шаги смещения стрелками |
| `ResizeMinSize` | 10 | нижний предел размера **жеста**; уже меньший элемент не раздувается |
| `IsResizeContainedToParent` | `true` | вложенный контрол не выходит за свою форму |
| `IsSnapToGridEnabled`, `SnapStep` | `true`, `NaN` | привязка к сетке; `NaN` — шаг сетки |
| `IsSnapToGuidesEnabled`, `SnapGuideTolerance` | `true`, 6 | направляющие выравнивания, радиус в пикселях экрана |
| `IsEqualSpacingEnabled` | `true` | равные интервалы |

Привязку, направляющие и интервалы исполняют [инструменты](../Surface.Editing/README.md); на голом
`SurfaceView` эти опции ничего не меняют.

`SurfaceCursors` (`SurfaceView.Cursors`) — курсоры жестов без собственного элемента: `Move`, `Blocked`,
`Pan`, `Marquee`, `Reorder`, `GuideHorizontal`, `GuideVertical`. `null` значит курсор библиотеки.
Курсор ставится на входе в жест, снимается на выходе с возвратом прежнего значения и через
`SetCurrentValue`, поэтому курсор, привязанный хостом, переживает жест.

Масштаб колесом забирает событие, только если масштаб действительно изменился: с заданными и не
нажатыми `ZoomModifiers` колесо уходит внешнему `ScrollViewer`. Щипок тачпада (`Delta` — приращение за
событие) и щипок пальцами (`Scale` накоплен от начала жеста) сходятся в `ZoomAt`; щипок пальцами
заодно панорамирует. Автопрокрутка работает у перетаскивания и рамки: жест хранит начало в мировых
координатах, поэтому сдвиг холста под неподвижным указателем его не искажает.

## Клавиатура

Клавиши — набор команд `SurfaceView.KeyCommands`. Нажатие проходит набор по порядку и достаётся первой
команде, которая его узнала **и** выполнилась; команда, которой нечего делать, уступает клавишу
следующей.

| Клавиши | Команда (`SurfaceKeyCommands`) |
| --- | --- |
| `Ctrl + Z`; `Ctrl + Y`, `Ctrl + Shift + Z` | `Undo`, `Redo` — поднимают `UndoRequested`/`RedoRequested` |
| `Alt` + стрелки, `Alt + Shift` + стрелки | `Resize` — двигает правый или нижний край |
| стрелки, `Shift` + стрелки | `Nudge` — смещение на `NudgeStep`/`LargeNudgeStep`, одна запись отмены на нажатие |
| `Esc` | `ClearSelection` |
| `Ctrl + A` | `SelectAll` — все контейнеры |
| `Delete`, `Backspace` | `Delete` — поднимает `DeleteRequested` |

```csharp
view.KeyCommands.Add(new SurfaceKeyCommand("app.rename", new KeyGesture(Key.F2), v => Rename(v)));
view.KeyCommands.Remove(SurfaceKeyCommands.ClearSelection);   // Escape нужен приложению
view.KeyCommands.Insert(0, command);                          // услышать клавишу раньше встроенных
```

Команда с занятым идентификатором в `Add` заменяет прежнюю на её месте. Второй конструктор
`SurfaceKeyCommand(id, matches, execute)` разделяет узнавание и исполнение. Клавиатура требует фокуса:
поверхность берёт его на нажатии указателя, если фокус не внутри неё. Уже обработанные нажатия она
пропускает, поэтому клавиши не отбираются у вложенного редактируемого контрола.

## Контракт изменений

Структуру дерева поверхность не правит (ADR 0001): она правит геометрию и `ZIndex` и сообщает об этом.

- `EditCompleted` — одна завершённая единица редактирования, **одно событие на жест**. Перетаскивание
  пяти элементов даёт одну запись с пятью изменениями; жест, не изменивший геометрию, события не
  поднимает.
- `SurfaceEditCompletedEventArgs.Kind` — `Move`, `Resize`, `Order`, `Group`; `Changes` — список
  `TargetChange`.
- `GeometryChange` — `Target`, `OldBounds`, `NewBounds` в координатах поверхности; `OrderChange` —
  `Target`, `OldZIndex`, `NewZIndex`. Изменения слоёв выше наследуют тот же `TargetChange`.
- `Revert(change)` и `Reapply(change)` применяют любое изменение, не поднимая `EditCompleted`.
  `ApplyGeometry(target, bounds)` и `ApplyOrder(target, zIndex)` — то же для одной величины.
- `SetTargetGeometry(target, bounds)` — правка снаружи жеста (панель свойств, команда): идёт через те
  же швы, что перетаскивание, открывает свою единицу и возвращает, принята ли правка. Политики жестов
  хоста не ограничивают: это просьба самого хоста.

Готовая история — `SurfaceHistory`: копит единицы редактирования, обслуживает `UndoRequested` и
`RedoRequested` и принимает структурные правки хоста реализацией `ISurfaceChange` через `Push`.

```csharp
var history = new SurfaceHistory(view);             // Undo/Redo/CanUndo/CanRedo/Changed/Clear

// Структурную правку выполняет хост, а в стек кладёт её вместе с правками поверхности.
elements.Insert(index, element);
history.Push(new InsertChange(elements, index, element));   // ISurfaceChange: Revert/Reapply
```

Своя история строится на тех же двух точках: `EditCompleted` копит, `Revert`/`Reapply` применяют.

## Запросы к хосту

Коллекцией владеет хост, поэтому удаление и история — запросы. Обход подписчиков останавливается на
первом, выставившем `Handled`; необработанное нажатие всплывает дальше.

| Событие | Аргументы | Когда |
| --- | --- | --- |
| `DeleteRequested` | `SurfaceDeleteRequestedEventArgs`: `Targets`, `Handled` | `Delete`/`Backspace` |
| `UndoRequested`, `RedoRequested` | `SurfaceHistoryRequestedEventArgs`: `Handled` | сочетания отмены и повтора |

```csharp
view.DeleteRequested += (_, e) =>
{
    foreach (int index in e.Targets
                 .Select(t => view.IndexFromContainer(t.Container))
                 .Where(i => i >= 0).Distinct().OrderByDescending(i => i))
        elements.RemoveAt(index);

    e.Handled = true;
};
```

## Контекстные действия

Правый клик или вызов `RequestContextAsync` собирают `SurfaceContextRequest`: `Scope`
(`Surface`, `Container`, `NestedTarget`, `Selection`), `Target`, `Selection`, `WorldPoint`,
`ViewportPoint`, `ScreenPoint`, `Modifiers`, `Source` (`Pointer`, `Keyboard`, `Programmatic`). Действия
дают провайдеры из `ContextActionProviders` (`ISurfaceContextActionProvider.GetActionsAsync`), показывает
`ContextPresenter` (по умолчанию `ContextMenuContextPresenter` на `ContextMenu`).

- `ContextMenuRequesting` до показа: `Cancel` отменяет контекст целиком, `Handled` означает «показал
  сам» — презентер молчит, `ContextMenuResolved` приходит.
- `ContextMenuResolved` после разрешения: `Request`, `Actions`, `Handled`.
- Новый запрос отменяет незавершённый предыдущий.

`SurfaceContextAction`: `Header`, `Command`, `CommandParameter`, `Icon`, `IsEnabled`, `IsVisible`,
`IsSeparator`, `Items` (подменю), `Group`, `Order`. Клавиатурного вызова ядро не назначает: хост,
которому он нужен, вызывает `RequestContextAsync(SurfaceContextSource.Keyboard, point, modifiers)`
своей командой.

## Ключи темы

| Ключ | Назначение |
| --- | --- |
| `Surface.BackgroundBrush` | фон поверхности |
| `Surface.MarqueeStrokeBrush`, `Surface.MarqueeFillBrush` | рамка выделения |
| `Surface.Selection.StrokeThickness` | толщина рамки выделения |
| `Surface.Grid.*` | сетка, см. «Сетка» |
| `SurfaceItem.SelectionBrush`, `SurfaceItem.HoverBrush`, `SurfaceItem.SelectionThickness` | контур контейнера ядра |

Кисти объявлены в `ThemeDictionaries`, отдельно для `Light` и `Dark`.

## Точки расширения

Слои выше подключаются к ядру через internal-швы, открытые им `InternalsVisibleTo`: геометрия target'а
(`ISurfaceGeometry`), кандидаты внутри контейнера (`ISurfaceTargetResolver`), политика взаимодействия
пересечением участников (`ISurfaceInteractionPolicy`), состав единицы правки (`IEditFacet`), поправка
позиции (`ISurfacePositionModifier`) и службы `AddService`/`GetService`. Публичными они станут под
первого внешнего потребителя; до тех пор редактор вне этого репозитория строится наследованием
`SurfaceView` и публичным API выше.
