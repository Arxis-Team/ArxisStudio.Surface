# 6. Имена следуют слоям: приставка Design уступает Surface

Дата: 2026-09-27
Статус: Принято

## Контекст

Библиотека выросла из монолита `ArxisStudio.DesignEditor`. ADR 0003 развёл её на слои и сборки,
репозиторий переименован в `ArxisStudio.Surface`, но типы сохранили имена монолита: в ядре —
`DesignGrid`, `DesignSelectionTarget`, `DesignEditorInputGestures`, `DesignEditorContextRequest`; в
инструментах — `DesignRuler`, `DesignGuide`, `DesignInteraction`; в дизайнере — `DesignEditor` и
`DesignEditorItem`. Ключи темы всех трёх слоёв начинались с `DesignEditor.`: при разделении на
сборки их оставили как есть ради совместимости тем приложений.

Имя говорило о происхождении, а не о слое. Ядро, которое не знает, что лежит на холсте, называло
свои типы по дизайнеру; редактор узлов пользовался `DesignSelectionTarget` и событием
`DesignSelectionChanged`, хотя дизайнера не видит. Класс `DesignEditor` редактирует любое дерево
контролов Avalonia — окно, `UserControl`, шаблон, — а не только формы.

## Решение

**Приставка — имя библиотеки, а не слоя-источника.** В ядре, инструментах и дизайнере `Design` и
`DesignEditor` заменены на `Surface`: `SurfaceGrid`, `SurfaceSelectionTarget`, `SurfaceInputGestures`,
`SurfaceRuler`, `SurfaceGuide`, `SurfaceGroup`, `SurfaceContentMode`. Так уже назывались
`SurfaceSnapping`, `SurfaceGuides` и `SurfaceMinimap` в инструментах.

**Главный контрол слоя — `<Слой>View`, контейнер — `<Слой>Item`**, как в ядре:
`ArxisStudio.Surface` → `SurfaceView`/`SurfaceItem`, `ArxisStudio.Surface.UiDesigner` →
`UiDesignerView`/`UiDesignerItem`. Имя `UiDesigner` совпало бы с пространством имён и сделало бы
неоднозначной ссылку из любого кода внутри `ArxisStudio.Surface`. Тема дизайнера —
`UiDesignerTheme.axaml`, демо — `samples/UiDesigner.Demo`, как `samples/Nodes.Demo`. Редактор узлов
остаётся `NodeEditor` с контейнером `Node`: имя по предмету правки в этой области общепринято.

**Два исключения.**

- Описания правок — `TargetChange`, `GeometryChange`, `OrderChange`, `GroupChange`. Имя
  `SurfaceChange` читалось бы как реализация существующего `ISurfaceChange`, а это разные вещи:
  запись истории откатывает себя сама, изменение target'а применяет поверхность
  (`SurfaceView.Revert`).
- В членах `SurfaceView`, где `Design` означало «design target», стоит `Target`: `SelectedTargets`,
  `SelectTarget`, `SetTargetGeometry`. Событие — `SurfaceSelectionChanged`: `SelectionChanged` занято
  `SelectingItemsControl` и работает на уровне контейнеров. `Layout.DesignX`/`DesignY` —
  координаты относительно панели элементов дизайнера, то есть координаты поверхности, — стали
  `SurfaceX`/`SurfaceY`.

**Ключ темы несёт имя слоя, который его объявляет**: `Surface.*` у ядра и инструментов,
`UiDesigner.*` у дизайнера, `UiDesignerItem.*` у контейнера дизайнера; `NodeEditor.*` и
`SurfaceMinimap.*` не менялись. Прежнее «ключи при разделении не переименованы» снято: ключ, названный
по исчезнувшему классу, пережил бы его.

## Отвергнутые варианты

- **Оставить имена.** Совместимость обещать нечем: версия `0.x`, в NuGet библиотека не публикуется,
  а все потребители — демо и образец `FormsDesigner` в ArxisStudio.ProjectSystem — переводятся тем
  же шагом.
- **`FormDesigner`** — сужает класс до форм.
- **`SurfaceEditor`** — читается как общий редактор поверхности, которому `NodeEditor` наследует, а
  он ему сосед.
- **`UiEditor`** — симметрично `NodeEditor`, но расходится с именем слоя и пакета.

## Последствия

- Изменение ломающее: имена типов, членов, файлов тем, ключей ресурсов. Хост меняет
  `avares://…/ArxisStudioDesignEditorTheme.axaml` на `avares://…/UiDesignerTheme.axaml` и ключи
  своих переопределений по таблице ниже.
- Формат документа хоста не меняется: пометку группы образец `FormsDesigner` хранит своим атрибутом
  `d:DesignGroup`, и это его формат, а не API библиотеки.
- Слепки публичной поверхности изменились только переименованиями: каждая прежняя строка,
  пропущенная через таблицу, совпадает с новой.
- Имена типов в ADR 0001–0005 приведены к новым; история в них — прежняя.

## Соответствие

Ядро, `ArxisStudio.Surface`:

| Было | Стало |
| --- | --- |
| `DesignGrid` | `SurfaceGrid` |
| `DesignSelectionTarget`, `DesignSelectionScope`, `DesignSelectionChangedEventArgs` | `SurfaceSelectionTarget`, `SurfaceSelectionScope`, `SurfaceSelectionChangedEventArgs` |
| `DesignEditorInputGestures`, `DesignEditorPointerButton` | `SurfaceInputGestures`, `SurfacePointerButton` |
| `DesignEditorInteractionOptions` | `SurfaceInteractionOptions` |
| `DesignEditorCursors` | `SurfaceCursors` |
| `DesignEditorDeleteRequestedEventArgs`, `DesignEditorHistoryRequestedEventArgs` | `SurfaceDeleteRequestedEventArgs`, `SurfaceHistoryRequestedEventArgs` |
| `DesignEditorContextAction`, `…Request`, `…Scope`, `…Source` | `SurfaceContextAction`, `…Request`, `…Scope`, `…Source` |
| `DesignEditorContextRequestedEventArgs`, `DesignEditorContextRequestingEventArgs` | `SurfaceContextRequestedEventArgs`, `SurfaceContextRequestingEventArgs` |
| `IDesignEditorContextActionProvider`, `IDesignEditorContextPresenter` | `ISurfaceContextActionProvider`, `ISurfaceContextPresenter` |
| `DesignEditKind`, `DesignEditCompletedEventArgs` | `SurfaceEditKind`, `SurfaceEditCompletedEventArgs` |
| `DesignChange`, `DesignGeometryChange`, `DesignOrderChange` | `TargetChange`, `GeometryChange`, `OrderChange` |
| `SurfaceView.SelectedDesignTargets`, `SelectedDesignTargetsCount` | `SelectedTargets`, `SelectedTargetsCount` |
| `SurfaceView.DesignSelectionChanged` | `SurfaceSelectionChanged` |
| `SurfaceView.SelectDesignTarget`, `SetDesignGeometry` | `SelectTarget`, `SetTargetGeometry` |

Инструменты, `ArxisStudio.Surface.Editing`:

| Было | Стало |
| --- | --- |
| `DesignInteraction` | `SurfaceInteraction` |
| `DesignRuler` | `SurfaceRuler` |
| `DesignGuide`, `DesignGuideOrientation` | `SurfaceGuide`, `SurfaceGuideOrientation` |
| `DesignGuideChangeKind`, `DesignGuideChangeRequestedEventArgs` | `SurfaceGuideChangeKind`, `SurfaceGuideChangeRequestedEventArgs` |
| `DesignSnapGuide`, `DesignSnapGuideKind`, `DesignSnapGuideOrientation` | `SurfaceSnapGuide`, `SurfaceSnapGuideKind`, `SurfaceSnapGuideOrientation` |
| `DesignSpacingHint` | `SurfaceSpacingHint` |

Дизайнер интерфейса, `ArxisStudio.Surface.UiDesigner`:

| Было | Стало |
| --- | --- |
| `DesignEditor`, `DesignEditorItem` | `UiDesignerView`, `UiDesignerItem` |
| `DesignEditorReorderRequestedEventArgs` | `UiDesignerReorderRequestedEventArgs` |
| `DesignContentMode` | `SurfaceContentMode` |
| `DesignGroup`, `DesignGroupInfo` | `SurfaceGroup`, `SurfaceGroupInfo` |
| `IDesignGroupStore`, `DesignGroupAttachedStore` | `ISurfaceGroupStore`, `SurfaceGroupAttachedStore` |
| `DesignGroupChange` | `GroupChange` |
| `Layout.DesignX`, `Layout.DesignY` | `Layout.SurfaceX`, `Layout.SurfaceY` |

Темы и ключи:

| Было | Стало |
| --- | --- |
| `ArxisStudio.Surface.UiDesigner/Themes/ArxisStudioDesignEditorTheme.axaml` | `…/Themes/UiDesignerTheme.axaml` |
| `DesignEditor.*` ядра и инструментов (`Grid.*`, `SelectionAdorner.*`, `SnapGuide*`, `UserGuide*`, `Ruler.*`, `SpacingBrush`, `BackgroundBrush`, `Marquee*`, `Selection.*`) | `Surface.*` с тем же хвостом |
| `DesignEditor.ReorderIndicatorBrush` | `UiDesigner.ReorderIndicatorBrush` |
| `DesignEditorItem.*` (`OutlineOpacity`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `HighlightBrush`) | `UiDesignerItem.*` с тем же хвостом |
