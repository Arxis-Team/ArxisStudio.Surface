# ArxisStudio.Surface.Nodes

Редактор узлов: `NodeEditor : SurfaceView` показывает граф приложения — узлы, их порты и связи между
портами — и даёт его править жестами. Связь знает свои концы **данными портов**, а не координатами:
где порт на холсте, редактор находит сам. Структуру графа правит приложение, редактор её только
просит (ADR 0001, 0004). Слой стоит рядом с [дизайнером интерфейса](../Surface.UiDesigner/README.md), а
не над ним, и сёстры друг друга не знают.

| | |
| --- | --- |
| Пакет и сборка | `ArxisStudio.Surface.Nodes`, `net8.0` |
| Пространство имён | `ArxisStudio.Surface.Nodes` |
| Адрес разметки | `https://github.com/Arxis-Team/ArxisStudio.Surface` |
| Тема | `avares://ArxisStudio.Surface.Nodes/Themes/ArxisStudioNodeEditorTheme.axaml` |
| Зависимости | `ArxisStudio.Surface`, `ArxisStudio.Surface.Editing` |

Общая механика — viewport, выделение узлов, клавиатура, контракт изменений, `SurfaceHistory` — в
[README ядра](../Surface/README.md); миникарта и привязка к сетке — в
[README инструментов](../Surface.Editing/README.md).

## Подключение

```xml
<Application.Resources>
    <ResourceInclude Source="avares://ArxisStudio.Surface.Nodes/Themes/ArxisStudioNodeEditorTheme.axaml" />
</Application.Resources>
```

Точка входа включает темы ядра, инструментов и `NodesTheme.axaml` самого слоя.

```xml
<surface:NodeEditor x:Name="Editor"
                    ItemsSource="{Binding Nodes}"
                    ItemLocationBinding="{Binding Location}"
                    ItemHeaderBinding="{Binding Title}"
                    Links="{Binding Links}"
                    LinkSourceBinding="{Binding From}"
                    LinkTargetBinding="{Binding To}">
    <surface:NodeEditor.DataTemplates>
        <!-- тело узла — порты; название узел рисует сам, из ItemHeaderBinding -->
        <DataTemplate x:DataType="vm:NodeViewModel">
            <StackPanel>
                <ItemsControl ItemsSource="{Binding Inputs}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="vm:PortViewModel">
                            <surface:Port Direction="Input" Data="{Binding}" Content="{Binding Name}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <!-- выходы — так же, с Direction="Output" -->
            </StackPanel>
        </DataTemplate>
    </surface:NodeEditor.DataTemplates>
</surface:NodeEditor>
```

- **Узлы** — `ItemsSource`; контейнер каждого — `Node : SurfaceItem`.
- **Заголовок** — `ItemHeaderBinding`, название узла из модели. Узел рисует его сам, над телом из
  шаблона; о полосе и цвете проводов — «Оформление узла».
- **Связи** — `Links`, любая коллекция данных приложения. Концы связи — ключи портов, которые достают
  `LinkSourceBinding` и `LinkTargetBinding`, как `DisplayMemberBinding`: в разметке они компилируются,
  тип данных компилятор берёт у `Links`.
- **Порт** — `Port : ContentControl` где угодно внутри шаблона узла. `Direction` — `Input` или
  `Output`, `Data` — ключ, по которому его находит связь (без него ключом служит `DataContext`).
  Конец связи — центр штырька `PART_Pin` в мировых координатах. `IsConnected` — есть ли у порта связь.

**Положение узла — из модели**, привязкой ядра `ItemLocationBinding`. `Location` двусторонний по
умолчанию, и ядро пишет его, не снимая привязки: перетаскивание уходит в модель, правка модели
двигает узел. С ней же панель держит контейнеры только у видимых узлов (см. README ядра,
«Виртуализация»), и у связи к свёрнутому узлу живого порта нет. Её конец тогда берётся так:

- узел показывался — нынешнее положение узла плюс смещение штырька в узле, снятое при последнем показе;
- не показывался ни разу — середина края его прямоугольника (правого у источника, левого у цели), если
  `PortNodeBinding` называет узел по данным порта:
  `PortNodeBinding="{Binding Node, DataType={x:Type vm:PortViewModel}}"`. Без неё такая связь не
  рисуется, пока узел не покажется.

Узел вне окна, поэтому оценку видно только углом, под которым кривая входит в окно; после показа
конец точный. Сдвиг свёрнутого узла моделью переносит концы его связей так же, как сдвиг видимого.

Связи виртуализуются вместе с узлами: у каждой есть запись в редакторе, а контрол `Link` — только у
видимой с тем же запасом и у той, что под курсором, под разрезом или отцепляется. Попадание, разрез,
выбор и миникарта идут по записям, поэтому выбранная связь остаётся выбранной и свёрнутой. Концы
связи, созданной редактором, даёт модель — привязками `LinkSourceBinding`/`LinkTargetBinding`, с
`INotifyPropertyChanged`, если хост меняет их на месте; готовая `Link` из коллекции развёрнута всегда,
и её концы хост меняет на ней самой.

Ставить положение кодом тоже можно:

```csharp
editor.ContainerPrepared += (_, e) =>
{
    if (e.Container is Node node && editor.ItemFromContainer(node) is NodeViewModel model)
        node.Location = model.Location;
};
```

Тогда положением распоряжается контейнер: итог перетаскивания хост получает из `EditCompleted`, а
перед удалением узла положение стоит забрать у контейнера, чтобы отмена вернула узел на место.

## Правка графа

Граф правит приложение по запросам. У каждого запроса есть `Handled`; обход подписчиков
останавливается на первом, выставившем его.

```csharp
editor.ConnectValidating += (_, e) => e.IsAllowed = graph.CanConnect(e.Source, e.Target, e.Link);
editor.ConnectRequested += (_, e) =>
{
    graph.Links.Add(new LinkViewModel(e.Source, e.Target));
    e.Handled = true;
};
editor.ReconnectRequested += (_, e) => e.Handled = graph.Reconnect(e.Link, e.End, e.NewPort);
editor.LinkDeleteRequested += (_, e) => e.Handled = graph.RemoveLinks(e.Links);
editor.LinkSplitRequested += (_, e) => e.Handled = graph.InsertReroute(e.Link, e.Location);
```

| Событие | Аргументы | Когда |
| --- | --- | --- |
| `ConnectValidating` | `Source`, `Target`, `Link`, `IsAllowed` | порт под протягиваемым концом; `Link` — связь, чей конец перецепляют, иначе `null` |
| `ConnectRequested` | `Source`, `Target`, `Handled` | отпускание новой связи на принимающем порте |
| `ReconnectRequested` | `Link`, `End` (`Source`/`Target`), `OldPort`, `NewPort`, `Handled` | отцеплённый конец брошен на другой порт |
| `LinkDeleteRequested` | `Links`, `Handled` | `Delete` по выбранным связям, конец брошен в пустоту, разрез |
| `LinkSplitRequested` | `Link`, `Location`, `Handled` | двойной щелчок по связи |

Правила графа — «во вход одна связь», запрет циклов, совместимость типов — живут в
`ConnectValidating`. Он спрашивается и о новой связи, и об отцеплённом конце и называет перецепляемую
связь: иначе «во вход одна связь» отказало бы связи, которая переносит свой же конец. **Источник —
всегда выход**, в какую сторону ни тянули.

Структурные правки хост кладёт в `SurfaceHistory` своей реализацией `ISurfaceChange` — тогда `Ctrl + Z`
отменяет их вместе с перетаскиванием узлов:

```csharp
graph.Links.Add(link);
history.Push(new LinkAdded(graph.Links, link));   // ISurfaceChange: Revert/Reapply
```

## Жесты

| Жест | Результат |
| --- | --- |
| протяжка от порта | новая связь; порт под концом показывает `:accepting` или `:refusing` ещё до отпускания |
| щелчок по связи | выбор; с `AdditiveSelectionModifiers` — переключение |
| `Delete` при выбранных связях | `LinkDeleteRequested` |
| протяжка тела связи | отцепляет ближний к нажатию конец: на порт того же направления — `ReconnectRequested`, в пустоту — `LinkDeleteRequested`, на свой порт — ничего |
| двойной щелчок по связи | `LinkSplitRequested` с точкой на кривой |
| `Alt` + протяжка по холсту или связи | разрез: перечёркнутые связи подсвечиваются `:cutting`, на отпускании уходят одним `LinkDeleteRequested` в порядке коллекции |
| `Esc` | отменяет протяжку и разрез, иначе снимает выбор связей |

- Перетаскиваемый узел садится на сетку, но не тянется к соседям: направляющих выравнивания и равных
  интервалов у редактора узлов по умолчанию нет — это инструмент макета, а не графа, и линии через
  холст на каждом перетаскивании только мешают. Нужны — `InteractionOptions.IsSnapToGuidesEnabled` и
  `IsEqualSpacingEnabled`; объект настроек, поставленный хостом целиком, приходит со своими значениями
  (у `SurfaceInteractionOptions` оба включены).
- Порт ловит протягиваемый конец в радиусе `PortCaptureRadius` (12 пикселей экрана) и притягивает к
  штырьку. Из портов, стоящих в одной точке (в пределах половины мировой единицы), берётся тот, что
  может принять связь, — так работают узлы перенаправления.
- Попадание по связи считает редактор с допуском `LinkHitTolerance` (6 пикселей экрана): сначала
  отсев по рамке связи, точное расстояние до кривой — только у прошедших. Наведение подсвечивает связь
  `:highlighted` по тому же правилу, что выбирает щелчок; над узлом связь не отвечает.
- Выбор связей (`SelectedLinks`, `SelectLink(link, additive)`, `ClearLinkSelection()`) и выбор узлов
  взаимоисключающие, поэтому `Delete` значит одно.
- Разрез — отрезок от нажатия до указателя. Его начало хранится в мировых координатах, автопрокрутка у
  края его не сдвигает. Точка ровно на связи считается лежащей по одну сторону, поэтому разрез через
  середину симметричной связи её режет. `LinkCutModifiers = None` выключает разрез. Правая кнопка
  занята контекстным меню, средняя — панорамой, `Ctrl` — режимом контейнеров ядра.
- Второе нажатие двойного щелчка выбирает связь, но отцепления не начинает; с модификатором добавления
  двойной щелчок — только жест выбора.

Клавиатурные команды слоя стоят в `KeyCommands` впереди встроенных и уступают клавишу, когда им нечего
делать: `NodeEditorKeyCommands.CancelLink` (`nodes.cancelLink`), `ClearLinkSelection`
(`nodes.clearLinkSelection`), `DeleteLinks` (`nodes.deleteLinks`).

## Излом связи: узел перенаправления

Излом — это узел, а не точка на связи (ADR 0005). Связь в библиотеке остаётся одной кубической кривой
от порта к порту; на двойной щелчок хост ставит в точку свой узел перенаправления и вместо одной
связи заводит две. Узел перенаправления выбирают, тянут, притягивают к сетке, удаляют и отменяют как любой узел.

`Reroute` — готовый компактный узел: вход и выход в одной точке в центре кольца. Хост заводит для
узла перенаправления свой тип данных и шаблон с `Reroute`; ключи портов задаются оба.

```xml
<DataTemplate x:DataType="vm:RerouteNode">
    <surface:Reroute Input="{Binding Inputs[0]}" Output="{Binding Outputs[0]}" />
</DataTemplate>
```

```csharp
editor.LinkSplitRequested += (_, e) =>
{
    double half = editor.TryFindResource("NodeEditor.Reroute.Size", editor.ActualThemeVariant, out var size)
                  && size is double d ? d / 2 : 0;
    var knot = graph.CreateReroute(e.Location - new Vector(half, half));   // Location — центр узла перенаправления
    graph.Split((LinkViewModel)e.Link, knot);                              // одна связь → две через knot
    e.Handled = true;
};
```

Шаблон узла перенаправления ставится **раньше** общего шаблона узла, если его тип наследует тип
узла. В `Node` узел перенаправления ставит узлу `:reroute`, и тема снимает с узла карточку. Нажатие по центру тянет связь
из выхода, по кольцу — перетаскивает узел.

Узел перенаправления смотрит по проводу, как в Blueprint (ADR 0013): если дальние концы исходящих связей
в среднем левее входящих, провод приходит в него справа и уходит влево, и обратный провод не
закручивается у кольца. Решает редактор и помнит решение и у свёрнутого узла. Повисший узел — без входа
или без выхода после снятия связей — снимает хост: оба примера уносят его вместе с проводом одной записью
истории.

## Миникарта

`SurfaceMinimap` из инструментов работает с редактором узлов как с любой поверхностью, а редактор
дорисовывает на ней разрешённые связи той же кривой, кистью `NodeEditor.Link.Stroke`.

```xml
<Grid>
    <surface:NodeEditor x:Name="Editor" ItemsSource="{Binding Nodes}" Links="{Binding Links}" />
    <surface:SurfaceMinimap Editor="{Binding #Editor}" Width="220" Height="150"
                            HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="12" />
</Grid>
```

## Оформление узла

Дизайн карточки, заголовка, портов и проводов задаёт хост, а без единой строки оформления редактор
рисует базовый (ADR 0009 библиотеки):

- **карточка** — скруглённая, с рамкой; под курсором и выбранная — своей рамкой; тело — шаблон узла с
  отступом;
- **область заголовка** — сверху, высотой не меньше `NodeEditor.Node.HeaderHeight` (24), залитая
  полосой, с названием полужирным. Нет ни заголовка, ни полосы — нет и области; заголовок без полосы
  лежит на фоне карточки цветом её текста. Название на полосе белое, а на светлой — чёрное: узел
  выбирает по относительной яркости полосы (порог WCAG 0,179), и контраст не ниже 4,5:1 при любом цвете
  хоста. Хост, заменивший цвета названия, держит контраст сам — порог выбран под белый и чёрный;
- **порт** — штырёк и подпись, связь приходит в центр штырька;
- **провод** — кубическая кривая пером темы; выбор, наведение, отцепление и разрез — своими цветами.

Модель даёт узлу и проводу по значению — привязками к своему элементу:

```xml
<surface:NodeEditor ItemsSource="{Binding Nodes}"
                    ItemLocationBinding="{Binding Location}"
                    ItemHeaderBinding="{Binding Title}"
                    ItemAccentBinding="{Binding Kind.Color}"
                    Links="{Binding Links}"
                    LinkSourceBinding="{Binding From}"
                    LinkTargetBinding="{Binding To}"
                    LinkStrokeBinding="{Binding DataType.Color}" />
```

- `ItemHeaderBinding` — заголовок, `Node.Header`: строка или любое содержимое для `HeaderTemplate`.
- `ItemAccentBinding` ядра — полоса, `SurfaceItem.Accent`: кисть или цвет, как категория узла в
  Blueprint. Узел показывает её под названием, упрощённая карточка — полосой той же высоты.
- `LinkStrokeBinding` — цвет провода, как тип данных в Blueprint: кисть или цвет, без значения — перо
  темы. Цвет модели — обычное состояние провода: выбранный, подсвеченный и перечёркнутый провод красится
  цветом темы, потому что обратная связь жеста важнее типа. Упрощённый вид рисует провода теми же
  цветами. Готовую `Link` из коллекции хост красит сам, её `Stroke`.

Цвета модели — данные, а не значения темы. Название на полосе подстраивается само, а провод хосту с
обеими темами стоит красить по теме: цвет, заметный на тёмном холсте, на светлом бывает слабее 3:1.

Заменяется всё по частям:

| Что | Чем |
| --- | --- |
| карточка и область заголовка | своя `ControlTheme` для `Node`: части `PART_Card`, `PART_Header`, `PART_HeaderPresenter`, `PART_ContentPresenter`, псевдоклассы `:header`, `:accent`, `:light-accent` |
| вид заголовка | `Node.HeaderTemplate` — стилем узла |
| тело узла | шаблон узла — `ItemTemplate` или `DataTemplates` |
| порт | своя тема `Port`; связь приходит в центр части `PART_Pin`, а без неё — в середину края порта |
| провод | своя тема `Link` или её ключи; цвет по модели — `LinkStrokeBinding` |
| цвета, толщины, отступы | ключи `NodeEditor.*` ниже |

Заголовок с иконкой — модель целиком в заголовок и свой шаблон:

```xml
<surface:NodeEditor ItemHeaderBinding="{Binding}" ...>
    <surface:NodeEditor.Styles>
        <Style Selector="surface|Node">
            <Setter Property="HeaderTemplate">
                <DataTemplate x:DataType="vm:NodeViewModel">
                    <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{Binding Icon}" Width="12" Height="12" />
                        <TextBlock Text="{Binding Title}" />
                    </StackPanel>
                </DataTemplate>
            </Setter>
        </Style>
    </surface:NodeEditor.Styles>
</surface:NodeEditor>
```

Псевдоклассы: `Port` — `:input`, `:output`, `:connected`, `:accepting`, `:refusing`; `Link` —
`:selected`, `:highlighted`, `:detaching`, `:cutting`; `Node` — `:selected`, `:dragging`, `:reroute`,
`:header` (есть заголовок или полоса), `:accent` (есть полоса), `:light-accent` (полоса светлая);
`Reroute` — `:selected`.

| Ключи | Что |
| --- | --- |
| `NodeEditor.Node.Background`, `…Foreground`, `…BorderBrush`, `…BorderThickness`, `…CornerRadius`, `…Padding`, `…HoverBorderBrush`, `…SelectedBorderBrush`, `…SelectedBorderThickness` | карточка узла |
| `NodeEditor.Node.HeaderHeight`, `…HeaderPadding`, `…HeaderForeground`, `…HeaderForegroundOnLight` | область заголовка: наименьшая высота — она же высота полосы упрощённой карточки, — отступ, название на тёмной и на светлой полосе; цвета названия вне вариантов темы — полосу красит хост |
| `NodeEditor.Port.PinSize`, `…PinMargin`, `…PinFill`, `…PinStroke`, `…PinStrokeThickness`, `…ConnectedFill`, `…AcceptingStroke`, `…RefusingStroke` | порт и штырёк |
| `NodeEditor.Link.Stroke`, `…Thickness`, `…SelectedStroke`, `…HighlightedStroke`, `…DetachingOpacity` | связь |
| `NodeEditor.PendingLink.Stroke` | протягиваемая связь |
| `NodeEditor.Cut.Stroke`, `NodeEditor.Cut.Thickness` | отрезок разреза |
| `NodeEditor.Reroute.Size`, `…RingThickness`, `…SelectedRingThickness` | узел перенаправления |
| `NodeEditor.Simplified.LinkThickness` | упрощённый вид: толщина связи в пикселях экрана |

Связи лежат в мировых координатах и масштабируются вместе с узлами; отрезок разреза рисуется своим
слоем поверх узлов, и его толщина задана в пикселях экрана.

## Стоимость

Два стенда, числа сборки `Release`.

`NodeGraphCostProbeTests` — графы из 200 и 2000 узлов без виртуализации:

- кадр перетаскивания пересчитывает ровно связи сдвинутого узла и переставляет только его; на 2000
  узлах кадр стоит 1,65 мс;
- поиск связи под указателем и порта под свободным концом — единицы микросекунд и на 2000 узлах;
- ход разреза через всё окно — около 9 мкс на 1950 связях;
- миникарта рисует готовую геометрию — единицы микросекунд при любом размере графа — и пересобирает
  её не чаще раза в 50 мс; сборка на Skia — около 1 мс на 2000 узлах.

`VirtualGraphCostProbeTests` — графы из 2000 и 10 000 узлов с `ItemLocationBinding` и
`PortNodeBinding`: развёрнуто одинаково — 35 узлов и 35 связей в окне 800 × 600; загрузка — 125 и
408 мс, кадр перетаскивания — 0,3–0,5 мс на обоих. Кадр панорамы внутри шага окна (ADR 0011, README
ядра) не меряет ничего — 0,002 мс, шаг — 0,03 мс на обоих графах. Ниже 50 % не развёрнуто ничего, и
слои рисуют только видимое своей операцией.

Вживую, Release-демо на 10 000 узлах, панорама по кадрам (`panBench`): 100 % и 30 % — медиана
16,7 мс, 60 кадров; 60 % — 27–31 мс: кадр — это отрисовка около 185 живых шаблонов узлов; «показать
всё» — 16,7 мс: карточки рисуются треугольниками, связи — путём на перо, Skia напрямую (ADR 0012).

Выбор без контейнеров (ADR 0010, README ядра): «выбрать всё» на 2000 и 10 000 узлах — 20–45 мс, и
развёрнуто столько же, сколько до выбора; кадр перетаскивания всего выбора — 36 и 99 мс: свёрнутые
узлы едут записью в модель, и почти вся цена — запись привязкой и её перечитывание. Узел порта
ни разу не показанного узла читается привязкой один раз, а не на каждом кадре.

Пул контейнеров не сжимается, и память держится по пику развёрнутого — его ограничивает порог
упрощённого вида.

## Упрощённый вид

Ниже `SimplifiedZoom` — у редактора узлов 0,3, у ядра 0,5 (ADR 0013) — узлы рисуются карточками без контейнеров (ADR 0008 библиотеки), а
связи — так: контрол есть у связи под курсором, под разрезом и отцепляемой и у связи развёрнутого
узла — перетаскиваемый узел ведёт свои связи вживую; остальные рисует слой связей — только видимые,
ломаной с числом отрезков по длине связи на экране, — выбранные — кистью выбора, в пиксель экрана,
окрашенные моделью — своими цветами. Карточка — копия
узла: фон и рамка — ключи карточки, полоса — из той же `ItemAccentBinding` высотой
`NodeEditor.Node.HeaderHeight`. Заголовок выше этой высоты — свой шаблон, крупный шрифт — карточка не
повторяет: высоту полосы она берёт у ключа, а не у узла.

Нажатие по карточке разворачивает узел и отдаёт нажатие ему: щелчок выбирает, протяжка двигает. Порт
на карточке не нажимается, пока узел свёрнут; на развёрнутом — как обычно. Наведение и нажатие над
карточкой связь под ней не берут. Выбранный узел остаётся карточкой в рамке выбора
(`NodeEditor.Node.SelectedBorderBrush`); выбор связей и узлов исключают друг друга и тогда, когда у
выбранных узлов нет контейнеров.

## Ограничения

- Перестановка, которая заново раскладывает элемент между портом и узлом, не трогая ни того, ни
  другого, конец связи не пересчитывает.
- Протягиваемая из узла перенаправления связь не разворачивается, пока не стала связью.

Рабочий хост со всеми запросами, узлом перенаправления, разрезом, миникартой и отменой — `samples/Nodes.Demo`.
