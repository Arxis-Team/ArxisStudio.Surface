# ArxisStudio.Surface.UiDesigner

Дизайнер интерфейса: `UiDesignerView : SurfaceView` редактирует деревья контролов Avalonia — окна,
`UserControl`, шаблоны, формы. Каждый корень лежит на холсте в своём контейнере `UiDesignerItem`, а
внутри выбираются, двигаются и меняют размер вложенные контролы — с учётом того, что позволяет их
панель-родитель. Структуру дерева дизайнер не правит: перестановку и удаление он просит у хоста
(ADR 0001).

| | |
| --- | --- |
| Пакет и сборка | `ArxisStudio.Surface.UiDesigner`, `net8.0` |
| Пространство имён | `ArxisStudio.Surface.UiDesigner`; стратегии размещения — `…UiDesigner.Placement`, internal |
| Адрес разметки | `https://github.com/Arxis-Team/ArxisStudio.Surface` |
| Тема | `avares://ArxisStudio.Surface.UiDesigner/Themes/UiDesignerTheme.axaml` |
| Зависимости | `ArxisStudio.Surface`, `ArxisStudio.Surface.Editing` |

Ниже **форма** — корень документа в контейнере, какого бы типа он ни был, **target** — то, что
выбрано: контейнер целиком или вложенный контрол. Общая механика поверхности — viewport, выделение,
клавиатура, контракт изменений, запросы — описана в [README ядра](../Surface/README.md), привязка,
направляющие, линейки и ручки — в [README инструментов](../Surface.Editing/README.md).

![Дизайнер интерфейса](../../docs/images/overview.png)

## Подключение

```xml
<Application.Resources>
    <ResourceInclude Source="avares://ArxisStudio.Surface.UiDesigner/Themes/UiDesignerTheme.axaml" />
</Application.Resources>
```

Точка входа собирает темы снизу вверх: ядро (`SurfaceTheme.axaml`), инструменты (`EditingTheme.axaml`),
свои словари. Значения — цвета, толщины, размер ручек — меняются ресурсами. Структуру шаблона
`UiDesignerView` заменить нельзя: он называет internal-типы (`UiDesignerPanel`,
`SelectionAdornerLayer`, конвертеры) и привязан к internal-свойствам. Значение, заданное атрибутом
внутри `ControlTemplate`, стилем приложения не перекрывается — поэтому расширяемые места темы
вынесены в ресурсы.

```xml
<surface:UiDesignerView ItemsSource="{Binding Screens}" SelectionMode="Multiple">
    <surface:UiDesignerView.Styles>
        <Style Selector="surface|UiDesignerItem" x:DataType="vm:ScreenViewModel">
            <Setter Property="Location" Value="{Binding Location, Mode=TwoWay}" />
            <Setter Property="Width" Value="{Binding Width, Mode=TwoWay}" />
            <Setter Property="Height" Value="{Binding Height, Mode=TwoWay}" />
            <Setter Property="ContentMode" Value="Loaded" />
        </Style>
    </surface:UiDesignerView.Styles>
    <surface:UiDesignerView.DataTemplates>
        <DataTemplate DataType="vm:HomeScreenViewModel">
            <views:HomeScreenView />
        </DataTemplate>
    </surface:UiDesignerView.DataTemplates>
</surface:UiDesignerView>
```

Корень, загруженный во время работы, отдают контейнеру как есть, без обёртки: лишний `ContentControl`
в режиме `Loaded` стал бы выбираемым элементом формы.

```csharp
editor.DataTemplates.Add(new FuncDataTemplate<FormViewModel>((form, _) => form.Root));

var root = (Control)AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path));
Forms.Add(new FormViewModel { Root = root, Location = new Point(80, 80), Width = 700, Height = 460 });
```

## Содержимое контейнера

`UiDesignerItem.ContentMode` (`SurfaceContentMode`) говорит, что в контейнере редактируется.

| Режим | Target'ом становится | Когда |
| --- | --- | --- |
| `Annotated` (по умолчанию) | контрол с `Layout.IsTracked` или `Layout.X`/`Y` | разметка написана вместе с приложением, автор сам помечает редактируемое |
| `Loaded` | любой авторский элемент; содержимое не реагирует на ввод | форма пришла целиком, например загружена из `.axaml` |

В режиме `Loaded` обход идёт по авторским связям — дети панели, ребёнок декоратора, контент-контрол, —
а не по визуальному дереву: внутренности контролов (текст кнопки, части шаблона) не выбираются. Ввод
гасится на презентере контента; выделение считается по прямоугольникам в мировых координатах, поэтому
от этого не страдает.

Фон контейнера — `Transparent`, и это не упущение: прозрачный фон участвует в попадании, и контейнер
отвечает на нажатие всей площадью. Форма без своего фона показывает холст; карточку под формой рисует
хост. Постоянный контур включает ресурс `UiDesignerItem.OutlineOpacity` (по умолчанию `0` — контур
только под курсором). Контур оверлейный и не отнимает у формы ни пикселя; `BorderThickness`
контейнера для этого не годится — `Border` сузил бы содержимое на толщину штриха.

## Координаты: `Layout`

`Layout` — присоединённые свойства позиционирования вложенных контролов.

| Свойство | Смысл |
| --- | --- |
| `Layout.X`, `Layout.Y` | положение относительно родителя; читает их `AbsolutePanel` |
| `Layout.SurfaceX`, `Layout.SurfaceY` | положение в координатах поверхности — относительно панели элементов дизайнера |
| `Layout.IsTracked` | включает отслеживание без `X`/`Y`; `Layout.Track`/`Untrack` делают то же кодом |

`X`/`Y` и `SurfaceX`/`SurfaceY` синхронизируются в обе стороны. Пересчёт идёт после прохода раскладки
через диспетчер, поэтому `SurfaceX`/`SurfaceY` отстают на один проход: читать их сразу после записи
`X`/`Y` бессмысленно. Где положением распоряжается панель, координата поверхности показывает
фактическое положение и перезаписывается.

`AbsolutePanel` ставит детей по `Layout.X`/`Y`, без них — по выравниванию, и публикует `Extent`.

```xml
<surface:AbsolutePanel>
    <TextBlock surface:Layout.X="200" surface:Layout.Y="100" Text="Заголовок" />
</surface:AbsolutePanel>
```

## Выделение

![Выделение вложенного контрола](../../docs/images/selection.png)

- Клик выбирает контрол **внутри** формы; клик по месту формы без target'ов выбирает контейнер.
- `Ctrl + Click` (`ContainerInteractionModifiers`) выбирает контейнер целиком, `Ctrl + Shift + Click` —
  добавляет контейнер.
- `Shift + Click` (`AdditiveSelectionModifiers`) добавляет или снимает контрол в пределах той же формы;
  контрол из другой формы в группу не добавляется. Обычный клик по уже выбранному участнику группы
  выделения не схлопывает, а делает его primary.
- Рамка, начатая **на пустом холсте**, набирает контейнеры; начатая **внутри формы** — её вложенные
  контролы. Владелец рамки — самый глубокий контейнер, целиком её содержащий; он пересчитывается на
  каждом шаге и доступен как `MarqueeScope`. Протяжка с пустого места формы по умолчанию тянет рамку;
  `ContainerEmptyAreaDrag="MoveContainer"` превращает её в перемещение контейнера.
- Правый клик мимо выделения переводит выделение на target под курсором, по выделению — оставляет его:
  меню относится ко всему выделенному.

![Множественное выделение](../../docs/images/multi-selection.png)

Состояние оверлея — свойства для привязок: `SelectionBounds` (рамка выделения в координатах
поверхности), `HasSingleSelection`, `HasMultipleSelection`, `HasMultipleContainerSelection`,
`HasMultipleNestedSelection`, `HasGroupSelection`. Каждый из нескольких выбранных вложенных контролов
получает свою рамку с ручками; несколько контейнеров — одну общую рамку. Навигация по выделению:
`CenterOnSelection()`, `FitSelectionToView()`.

## Раскладка и её возможности

Дизайнер знает панель-родителя вложенного контрола и не предлагает жест, который она не выполнит.
Таблица снята с Avalonia 12 (`LayoutHonourProbeTests`):

| Родитель | Явный `Width`/`Height` | `Layout.X`/`Y` | Перетаскивание |
| --- | --- | --- | --- |
| `AbsolutePanel` | учитывает | учитывает | задаёт позицию |
| `Canvas` | учитывает | игнорирует | задаёт `Canvas.Left`/`Top` |
| `StackPanel`, `WrapPanel` | учитывает | игнорирует | переставляет среди соседей |
| `Grid`, `DockPanel`, контент-хосты | учитывает | игнорирует | недоступно |

Изменение размера осмысленно везде: явный размер применяется до выравнивания. Действующая политика —
пересечение пользовательской (`SurfaceInteraction`) и возможностей раскладки; заблокированный жест не
выполняется молча, а теряет свою ручку или курсор. Для панели свойств:

```csharp
editor.SurfaceSelectionChanged += (_, _) =>
    status.Text = $"{editor.PrimarySelectionPlacement}: move {editor.PrimarySelectionMovePolicy}";
```

`PrimarySelectionPlacement` — `Absolute`, `Canvas`, `Stack`, `Grid`, `Dock`, `ContentHost`;
`PrimarySelectionMovePolicy` и `PrimarySelectionResizePolicy` — действующие политики.

**Ограничение формой.** При `InteractionOptions.IsResizeContainedToParent` (по умолчанию) вложенный
контрол не выходит за границы своей формы — не прямого родителя: панель, растущая по содержимому,
границей быть не может. Ограничивается только тянущаяся ось; `MinWidth`/`MaxWidth`/`MinHeight`/
`MaxHeight` контрола сильнее, при конфликте побеждает минимум.

## Перестановка среди соседей

В `StackPanel` и `WrapPanel` перетаскивание меняет порядок детей. Во время протяжки рисуется индикатор
точки вставки (`IsReordering`, `ReorderIndicator`), на отпускании поднимается запрос:

```csharp
editor.ReorderRequested += (_, e) =>
{
    document.Move(e.Target, e.OldIndex, e.NewIndex);   // структуру правит владелец разметки
    e.Handled = true;
};
```

- Без подписчика жест не начинается.
- `UiDesignerReorderRequestedEventArgs`: `Target`, `OldIndex`, `NewIndex` (индексы в `Panel.Children`),
  `Anchor` — сосед, перед которым встаёт контрол, `null` в конце; `Anchor` переживает любое
  представление дерева у хоста.
- Обход подписчиков останавливается на первом, выставившем `Handled`. Точка вставки вне границ
  отклоняется, а не подгоняется.
- Перестановка — структурная правка, в `EditCompleted` она не попадает; её отмена — дело хоста.

## Порядок перекрытия и распределение

`BringToFront()`, `SendToBack()`, `BringForward()`, `SendBackward()` меняют `ZIndex` выбранных среди
соседей по родителю, сохраняя их взаимный порядок, и возвращают, изменилось ли что-нибудь. `ZIndex`
в группе нормализуется в `0..n-1`. Правка идёт в `EditCompleted` с `Kind = Order` и `OrderChange`.

`DistributeHorizontally()`, `DistributeVertically()` делают равными **зазоры** между выбранными (не
расстояния между центрами); крайние стоят. Нужно не меньше трёх элементов; заблокированный
промежуточный отменяет операцию. Одна единица редактирования на операцию.

## Группы

![Группы](../../docs/images/groups.png)

Группа — пометка на контролах, а не новый узел разметки: дизайнер дерево не правит. Пометка — путь от
внешней группы к внутренней:

```xml
<Border surface:SurfaceGroup.Id="toolbar/icons" />
```

```csharp
editor.GroupSelection();                                // из текущего выделения; CanGroupSelection()
editor.UngroupSelection();                              // снять один внешний уровень
editor.RenameGroup(container, "group-1", "toolbar");    // один сегмент пути

foreach (SurfaceGroupInfo group in editor.GetGroups(container))    // дерево групп формы
    editor.GetGroupMembers(container, group.Path);                  // весь состав с вложенными
```

- Группа не выходит за пределы одной формы.
- Группировка считает **кластеры** — выбранную целиком группу или одиночный контрол — и требует двух.
  Группа-участник вкладывается в новую, а не растворяется.
- Роспуск снимает один внешний уровень; вложенные группы поднимаются выше.
- Повторный клик по тому же месту спускается на уровень: внешняя группа → вложенная → контрол.
- Группировка и переименование — единицы редактирования (`Kind = Group`, `GroupChange` с `OldId`/
  `NewId`) и отменяются наравне с перемещением.
- Чтение — запрос, а не снимок: `GetGroups` и `GetGroupMembers` считают по дереву в момент вызова.

**Где хранится пометка, решает владелец документа** (ADR 0002). Умолчание —
`SurfaceGroupAttachedStore.Default`, присоединённое свойство `SurfaceGroup.Id` на контроле. Хост, которому
пометка в разметке не нужна, задаёт своё хранилище:

```csharp
editor.GroupStore = new DocumentGroupStore(document);   // ISurfaceGroupStore: GetGroup, SetGroup, GroupsChanged
```

О правках, сделанных мимо дизайнера, хранилище сообщает `GroupsChanged`; подписка на него слабая.

## Направляющие, линейки, привязка

Дизайнер подключает все инструменты и берёт их свойства себе: `Guides`, `ShowGuides`, `ShowSnapGuides`,
`ShowRulers`, опубликованные `SnapGuides`, `SpacingHints`, `UserGuides`, `GuidePreview` и запрос
`GuideChangeRequested`. Как они работают — в [README инструментов](../Surface.Editing/README.md).

![Направляющие выравнивания](../../docs/images/snap-guides.png)

![Равные интервалы](../../docs/images/equal-spacing.png)

![Линейки и направляющие](../../docs/images/rulers-guides.png)

## Контекстное меню

![Контекстное меню](../../docs/images/context-menu.png)

Контекст собирает ядро; дизайнер определяет `Scope` по самому target'у под курсором: пустой холст —
`Surface`, контейнер — `Container`, вложенный контрол — `NestedTarget`, target внутри текущего
выделения — `Selection`. Действия даёт провайдер хоста (`ISurfaceContextActionProvider`); пример —
`UiDesignerDemoContextActionsProvider` в демо.

## Ключи темы

| Ключ | Назначение |
| --- | --- |
| `UiDesigner.ReorderIndicatorBrush` | индикатор точки вставки |
| `UiDesignerItem.OutlineOpacity` | контур контейнера в покое, `0` — только под курсором |
| `UiDesignerItem.BorderBrush`, `UiDesignerItem.BorderThickness`, `UiDesignerItem.CornerRadius` | контейнер |

Сетка, рамка выделения, ручки, направляющие и линейки — ключами `Surface.*` ядра и инструментов.
`SelectionRectangleStyle` задаёт тему прямоугольника рамки выделения.

## Известные ограничения

- **Загруженная форма глуха к указателю, но не к клавиатуре.** В режиме `Loaded` её контролы остаются в
  порядке обхода `Tab`: фокус может уйти в `TextBox` формы, и ввод поменяет макет, а `Delete` и
  `Ctrl + A` достанутся контролу, а не дизайнеру.
- **Свой шаблон `UiDesignerView` не написать** без форка темы — см. «Подключение».

Рабочий хост со всеми запросами, панелями групп и свойств, отменой и каналом автоматизации —
`samples/UiDesigner.Demo`.
