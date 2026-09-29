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
editor.ConnectDropped += (_, e) => menu.OpenFor(e.Port, e.Location, e.ViewportPoint);
```

| Событие | Аргументы | Когда |
| --- | --- | --- |
| `ConnectValidating` | `Source`, `Target`, `Link`, `IsAllowed` | порт под протягиваемым концом; `Link` — связь, чей конец перецепляют, иначе `null` |
| `ConnectRequested` | `Source`, `Target`, `Handled` | отпускание новой связи на принимающем порте |
| `ReconnectRequested` | `Link`, `End` (`Source`/`Target`), `OldPort`, `NewPort`, `Handled` | отцеплённый конец брошен на другой порт |
| `LinkDeleteRequested` | `Links`, `Handled` | `Delete` по выбранным связям, конец брошен в пустоту, разрез |
| `LinkSplitRequested` | `Link`, `Location`, `Handled` | двойной щелчок по связи |
| `ConnectDropped` | `Port`, `Direction`, `Location`, `ViewportPoint` | новая связь отпущена мимо портов; сообщение без `Handled` (ADR 0018) |

Правила графа — «во вход одна связь», запрет циклов, совместимость типов — живут в
`ConnectValidating`. Он спрашивается и о новой связи, и об отцеплённом конце и называет перецепляемую
связь: иначе «во вход одна связь» отказало бы связи, которая переносит свой же конец. **Источник —
всегда выход**, в какую сторону ни тянули.

`ConnectDropped` — повод для меню действий, как у Blueprint: провод из пина, брошенный в пустоту,
открывает у хоста список узлов, способных его принять, и новый узел встаёт в `Location` уже
подключённым. Меню хост вызывает сам — `RequestContextAsync(SurfaceContextSource.Programmatic,
e.ViewportPoint)` своим поставщиком действий. Связь, отпущенная на отказавший порт или на свой же порт,
сюда не приходит; отцеплённый конец в пустоте по-прежнему просит `LinkDeleteRequested`.

Структурные правки хост кладёт в `SurfaceHistory` своей реализацией `ISurfaceChange` — тогда `Ctrl + Z`
отменяет их вместе с перетаскиванием узлов:

```csharp
graph.Links.Add(link);
history.Push(new LinkAdded(graph.Links, link));   // ISurfaceChange: Revert/Reapply
```

## Жесты

| Жест | Результат |
| --- | --- |
| протяжка от порта | новая связь; порт под концом показывает `:accepting` или `:refusing` ещё до отпускания; отпущенная мимо портов — `ConnectDropped` |
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

## Роли пинов: данные, выполнение, делегат

Как в Blueprint (ADR 0016, 0017): данные — значения, выполнение — порядок узлов, делегат — событие,
переданное как значение, чтобы вызвать его позже (Event Dispatcher). Какая у пина и провода роль,
решает хост; базовый вид роли даёт тема:

| Роль | Пин | Провод |
| --- | --- | --- |
| `Data` | тема порта | тема провода |
| `Execution` | пятиугольник, `NodeEditor.Pin.Execution.Brush` | `NodeEditor.Link.Execution.Stroke`, `.Thickness` (3,5) |
| `Delegate` | квадрат у выхода, круг у входа, `NodeEditor.Pin.Delegate.Brush` | `NodeEditor.Link.Delegate.Stroke`, `.Thickness` (2) |

```xml
<surface:NodeEditor LinkRoleBinding="{Binding Role}" LinkStrokeBinding="{Binding Color}" ...>
    <!-- роль и цвет данных — привязкой к модели порта -->
    <surface:Port Direction="Input" Data="{Binding}" Content="{Binding Name}"
                  PinRole="{Binding Role}" PinBrush="{Binding Brush}" />
</surface:NodeEditor>

<!-- или стилем по классу порта -->
<Style Selector="surface|Port.flow">
    <Setter Property="PinRole" Value="Execution" />
</Style>
```

Вид складывается из трёх источников, сильнейший первым: значения хоста, роль, тема. `null` у хоста —
«вид роли»: привязка `PinBrush`, дающая цвет только портам данных, не гасит роль у портов выполнения
того же шаблона.

| API | Что делает |
| --- | --- |
| `Port.PinRole` | роль пина: `Data`, `Execution`, `Delegate` |
| `Port.PinShape` | `Circle`, `Execution` (пятиугольник остриём вправо), `Square`, `Diamond`, `Triangle`, `Custom`; `null` — форма роли |
| `Port.PinGeometry` | геометрия для `Custom`, вписывается в штырёк с сохранением пропорций |
| `Port.PinBrush` | обводка пустого штырька и заливка подключённого; `null` — цвет роли, у данных кисти темы |
| `Port.ActualPinShape`, `ActualPinBrush`, `PinData` | итог вида — для своей темы порта |
| `NodeEditor.LinkRoleBinding` | роль провода из модели: цвет и толщина роли из темы |
| `NodeEditor.LinkThicknessBinding` | толщина провода из модели, в мировых единицах; сильнее роли |

Правил у роли нет: выполнение только к выполнению, делегат только к делегату, сколько связей из выхода
и во вход — решает `ConnectValidating` хоста. Цвет и толщину провода держит запись связи: упрощённый
вид, маркеры и импульсы видят цвет роли; смена варианта темы его перечитывает. Упрощённый слой и
миникарта рисуют все провода одной толщиной.

## Кривая провода

Провод — кубическая кривая с горизонтальными касательными: из выхода вправо, во вход слева. Длина
касательной — по правилу Blueprint (ADR 0015): `min(|dx|, предел) · множитель + min(|dy|, предел) ·
множитель`, плечо кривой — треть. Вперёд — 1000 и 1 по обеим осям, назад — 200 и 3 по горизонтали,
200 и 1,5 по вертикали; плечо назад не короче 40. Числа задаёт `NodeEditor.LinkCurve`:

```csharp
editor.LinkCurve = new LinkCurve { BackwardHorizontalFactor = 2, BackwardVerticalFactor = 1 };
```

Новое значение пересчитывает все провода; правка полей уже заданного объекта — нет. Длинный почти
горизонтальный провод назад остаётся вытянутой петлёй у портов, как в Blueprint; круглую даёт узел
перенаправления на нём.

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
или без выхода после снятия связей — снимает хост; оба образца уносят его вместе с проводом одной записью
истории (`Stranded` в документе образца).

## Поток по проводам: импульсы и маркеры

Как отладка Blueprint (ADR 0014): сработавший провод вспыхивает, и по нему от выхода ко входу бегут
пузыри. Импульс зажигает хост одним вызовом, а гаснет он сам; повтор по горящему проводу продлевает его,
не сбивая бег.

```csharp
editor.PulseLink(link);                              // вид — LinkPulse редактора, кисти — тема
editor.PulseLink(link, new LinkPulse { Shape = LinkShape.Arrow, Speed = 300, GlowThickness = 0 });
editor.ClearLinkPulses();                            // остановили отладку — погасить всё
```

`PulseLink` возвращает `false`, если связи нет в `Links` или импульсы выключены. Объект `LinkPulse`
читается на каждом кадре: вид на связь заводят один раз и не меняют, а для другого вида создают новый.

Маркер — стрелка или другая фигура на проводе: всем сразу или каждой связи по модели.

```xml
<surface:NodeEditor LinkMarkerBinding="{Binding Marker}"
                    IsLinkPulseEnabled="{Binding Settings.ShowPulses}"
                    AreLinkMarkersVisible="{Binding Settings.ShowMarkers}">
    <surface:NodeEditor.LinkMarker>
        <surface:LinkMarker Shape="Chevron" Size="12" Spacing="80" />
    </surface:NodeEditor.LinkMarker>
</surface:NodeEditor>
```

| API | Что делает |
|---|---|
| `LinkCurve` | изгиб проводов: пределы и множители касательной вперёд и назад (ADR 0015) |
| `PulseLink(link, pulse?)` / `ClearLinkPulses()` | зажечь импульс по проводу / погасить все |
| `IsLinkPulseEnabled` | выключатель импульсов: выключенный гасит горящие и не зажигает новые |
| `LinkPulse` | вид импульса по умолчанию: `Shape`, `Geometry`, `Size`, `Speed` (192), `Spacing` (64), `Lifetime` (1 с), `FadeIn`, `FadeOut`, `Brush`, `GlowBrush`, `GlowThickness` |
| `LinkMarker` / `LinkMarkerBinding` | маркер всех проводов / свой у каждой связи; `null` из привязки — без маркера |
| `AreLinkMarkersVisible` | выключатель маркеров, настройку не трогает |
| `LinkMarker` (класс) | `Shape`, `Geometry`, `Size` (14), `Spacing` (0 — один), `Position` (0,5), `Brush`, `MinScreenSize` (4) |
| `LinkShape` | `Circle`, `Arrow`, `Chevron`, `Diamond`, `Square`, `Custom` — своя геометрия в квадрате −1…1 остриём по X |

Вид по умолчанию — ключи темы: `NodeEditor.LinkPulse.Brush`, `.GlowBrush`, `.Size`, `.GlowThickness`,
`.GlowOpacity`, `NodeEditor.LinkMarker.Brush`. Размеры — в мировых единицах, как у провода. Импульсы и
маркеры видны и в упрощённом виде: слой рисует по записям связей, а не по их контролам. Кадры
анимации слой просит, только пока горит импульс.

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
| карточка и область заголовка | своя `ControlTheme` для `Node`: части `PART_Card`, `PART_Header`, `PART_HeaderPresenter`, `PART_ContentPresenter`, `PART_Outline`, псевдоклассы `:header`, `:accent`, `:light-accent` |
| выбор и подсветка узла | `PART_Outline` — обводка поверх карточки толщиной `NodeEditor.Node.SelectedBorderThickness`, места не занимает; тема красит её на `:selected`, хост — для своих состояний (`surface|Node.active /template/ Border#PART_Outline`). Рамку карточки для этого не утолщают: содержимое уехало бы внутрь, и узел прыгал бы на пиксель |
| вид заголовка | `Node.HeaderTemplate` — стилем узла |
| тело узла | шаблон узла — `ItemTemplate` или `DataTemplates` |
| штырёк | `Port.PinRole`, `PinShape`, `PinGeometry`, `PinBrush` — привязкой или стилем; ключи ролей `NodeEditor.Pin.*` |
| порт | своя тема `Port`; штырёк — `Path#PART_Pin` с `Data` = `PinData`, цвет — `ActualPinBrush`; связь приходит в центр `PART_Pin`, а без неё — в середину края порта |
| провод | своя тема `Link` или её ключи; роль, цвет и толщина по модели — `LinkRoleBinding`, `LinkStrokeBinding`, `LinkThicknessBinding` |
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

Псевдоклассы: `Port` — `:input`, `:output`, `:connected`, `:accepting`, `:refusing`, `:pin-brush`; `Link` —
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
| `NodeEditor.LinkPulse.Brush`, `…GlowBrush`, `…Size`, `…GlowThickness`, `…GlowOpacity` | импульс по проводу |
| `NodeEditor.Pin.Execution.Brush`, `NodeEditor.Pin.Delegate.Brush` | пин роли выполнения и делегата |
| `NodeEditor.Link.Execution.Stroke`, `…Thickness`, `NodeEditor.Link.Delegate.Stroke`, `…Thickness` | провод роли выполнения и делегата |
| `NodeEditor.LinkMarker.Brush` | маркер провода без цвета модели |
| `NodeEditor.Simplified.LinkThickness` | упрощённый вид: толщина связи в пикселях экрана |

Связи лежат в мировых координатах и масштабируются вместе с узлами; отрезок разреза рисуется своим
слоем поверх узлов, и его толщина задана в пикселях экрана.

## Стоимость

Два стенда и живые замеры, числа сборки `Release`.

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
ядра) не меряет ничего — 0,002 мс, шаг — 0,03 мс на обоих графах. Ниже порога упрощённого вида не
развёрнуто ничего, и слои рисуют только видимое своей операцией.

Панорама по кадрам (`panBench` демо) на плотной сетке 10 000 узлов, медиана кадра:

| Масштаб | Развёрнуто узлов | Кадр | Чем занят кадр |
| --- | --- | --- | --- |
| 100 % | 63 | 16,7 мс | — |
| 60 % | 185 | 27–31 мс | отрисовка живых шаблонов узлов |
| 40 % | 385 | 48 мс | то же |
| 32 % | около 600 | 66 мс | то же |
| 25 %, «показать всё» | 0 | 16,7 мс | карточки треугольниками, связи путём на перо (ADR 0012) |

Между 30 и 50 % узлы живые (ADR 0013): маленькому графу это ничего не стоит, большому хост поднимает
`SimplifiedZoom`.

Выбор без контейнеров (ADR 0010, README ядра): «выбрать всё» на 2000 и 10 000 узлах — 20–45 мс, и
развёрнуто столько же, сколько до выбора; кадр перетаскивания всего выбора — 36 и 99 мс: свёрнутые
узлы едут записью в модель, и почти вся цена — запись привязкой и её перечитывание. Узел порта
ни разу не показанного узла читается привязкой один раз, а не на каждом кадре.

Пул контейнеров не сжимается, и память держится по пику развёрнутого — его ограничивает порог
упрощённого вида.

## Упрощённый вид

Ниже `SimplifiedZoom` узлы рисуются карточками без контейнеров (ADR 0008). У `NodeEditor` умолчание
свойства — 0,3, у ядра — 0,5 (ADR 0013).

- Контрол связи есть у связи под курсором, под разрезом, отцепляемой и у связи развёрнутого узла:
  перетаскиваемый узел ведёт свои связи вживую. Остальные рисует слой связей — только видимые,
  ломаной с числом отрезков по длине на экране; выбранные — кистью выбора, окрашенные моделью — своими
  цветами, толщина — в пикселях экрана.
- Карточка повторяет узел: фон и рамка — ключи карточки, полоса — из `ItemAccentBinding` высотой
  `NodeEditor.Node.HeaderHeight`. Заголовок выше этой высоты или крупным шрифтом карточка не повторяет:
  высоту полосы она берёт у ключа.
- Импульсы и маркеры рисуются и здесь: их слой работает по записям связей.

Нажатие по карточке разворачивает узел и отдаёт нажатие ему: щелчок выбирает, протяжка двигает. Порт
на карточке не нажимается, пока узел свёрнут; на развёрнутом — как обычно. Наведение и нажатие над
карточкой связь под ней не берут. Выбранный узел остаётся карточкой в рамке выбора
(`NodeEditor.Node.SelectedBorderBrush`); выбор связей и узлов исключают друг друга и тогда, когда у
выбранных узлов нет контейнеров.

## Ограничения

- Перестановка, которая заново раскладывает элемент между портом и узлом, не трогая ни того, ни
  другого, конец связи не пересчитывает.
- Протягиваемая связь не разворачивается у узла перенаправления и не несёт ни маркеров, ни импульсов,
  пока не стала связью.
- Узел перенаправления, оставшийся без входа или выхода, редактор не удаляет: граф правит хост.

Образцы: `samples/Nodes.Demo` — все запросы, узлы перенаправления, разрез, миникарта, 10 000 узлов,
роли выполнения и делегата (наблюдатель, как Event Dispatcher), импульсы по волне вычисления, маркеры по
типу провода привязкой; `samples/Nodes.StateMachine` — роль выполнения стилем, импульсы по сработавшему переходу,
маркер редактора с выбором фигуры, контекстное меню; `samples/Nodes.Calculator` — граф в духе Blueprint:
компактные узлы математики, литералы у неподключённых пинов, палитра действий с поиском по правой кнопке и
по `ConnectDropped`, узел преобразования между типами, «Играть» с импульсами, строками на экране и журналом.
