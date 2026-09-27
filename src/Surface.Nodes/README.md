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
                    Links="{Binding Links}"
                    LinkSourceBinding="{Binding From}"
                    LinkTargetBinding="{Binding To}">
    <surface:NodeEditor.DataTemplates>
        <DataTemplate x:DataType="vm:NodeViewModel">
            <StackPanel>
                <TextBlock Text="{Binding Title}" />
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
- **Связи** — `Links`, любая коллекция данных приложения. Концы связи — ключи портов, которые достают
  `LinkSourceBinding` и `LinkTargetBinding`, как `DisplayMemberBinding`: в разметке они компилируются,
  тип данных компилятор берёт у `Links`.
- **Порт** — `Port : ContentControl` где угодно внутри шаблона узла. `Direction` — `Input` или
  `Output`, `Data` — ключ, по которому его находит связь (без него ключом служит `DataContext`).
  Конец связи — центр штырька `PART_Pin` в мировых координатах. `IsConnected` — есть ли у порта связь.

**Положение узлу ставит хост**, когда готов контейнер, а не привязкой стиля: перетаскивание пишет
`Location` локальным значением, и привязка ему проиграла бы.

```csharp
editor.ContainerPrepared += (_, e) =>
{
    if (e.Container is Node node && editor.ItemFromContainer(node) is NodeViewModel model)
        node.Location = model.Location;
};
```

Итог перетаскивания хост получает из `EditCompleted`; перед удалением узла положение стоит забрать у
контейнера, чтобы отмена вернула узел на место.

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

- Порт ловит протягиваемый конец в радиусе `PortCaptureRadius` (12 пикселей экрана) и притягивает к
  штырьку. Из портов, стоящих в одной точке (в пределах половины мировой единицы), берётся тот, что
  может принять связь, — так работают перевалки.
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

## Излом связи: перевалка

Излом — это узел, а не точка на связи (ADR 0005). Связь в библиотеке остаётся одной кубической кривой
от порта к порту; на двойной щелчок хост ставит в точку свой узел-перевалку и вместо одной связи
заводит две. Перевалку выбирают, тянут, притягивают к сетке, удаляют и отменяют как любой узел.

`Reroute` — готовый компактный узел: вход и выход в одной точке в центре кольца. Хост заводит для
перевалки свой тип данных и шаблон с `Reroute`; ключи портов задаются оба.

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
    var knot = graph.CreateReroute(e.Location - new Vector(half, half));   // Location — центр перевалки
    graph.Split((LinkViewModel)e.Link, knot);                              // одна связь → две через knot
    e.Handled = true;
};
```

Шаблон перевалки ставится **раньше** общего шаблона узла, если тип перевалки наследует тип узла. В
`Node` перевалка ставит узлу `:reroute`, и тема снимает с узла карточку. Нажатие по центру тянет связь
из выхода, по кольцу — перетаскивает узел. Касательные у перевалки горизонтальны, как у любого порта:
связь, уведённая через перевалку назад, петляет.

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

## Оформление

Псевдоклассы: `Port` — `:input`, `:output`, `:connected`, `:accepting`, `:refusing`; `Link` —
`:selected`, `:highlighted`, `:detaching`, `:cutting`; `Node` — `:selected`, `:dragging`, `:reroute`;
`Reroute` — `:selected`.

| Ключи | Что |
| --- | --- |
| `NodeEditor.Node.Background`, `…Foreground`, `…BorderBrush`, `…BorderThickness`, `…CornerRadius`, `…Padding`, `…HoverBorderBrush`, `…SelectedBorderBrush`, `…SelectedBorderThickness` | карточка узла |
| `NodeEditor.Port.PinSize`, `…PinMargin`, `…PinFill`, `…PinStroke`, `…PinStrokeThickness`, `…ConnectedFill`, `…AcceptingStroke`, `…RefusingStroke` | порт и штырёк |
| `NodeEditor.Link.Stroke`, `…Thickness`, `…SelectedStroke`, `…HighlightedStroke`, `…DetachingOpacity` | связь |
| `NodeEditor.PendingLink.Stroke` | протягиваемая связь |
| `NodeEditor.Cut.Stroke`, `NodeEditor.Cut.Thickness` | отрезок разреза |
| `NodeEditor.Reroute.Size`, `…RingThickness`, `…SelectedRingThickness` | перевалка |

Связи лежат в мировых координатах и масштабируются вместе с узлами; отрезок разреза рисуется своим
слоем поверх узлов, и его толщина задана в пикселях экрана.

## Стоимость

Стенд `NodeGraphCostProbeTests` на графах из 200 и 2000 узлов, числа сборки `Release`:

- кадр перетаскивания пересчитывает ровно связи сдвинутого узла при любом размере графа; сам кадр на
  2000 узлах — около 2,5 мс и в 18 раз дороже, чем на 200: растёт он через раскладку и отрисовку всех
  узлов;
- поиск связи под указателем и порта под свободным концом — единицы микросекунд и на 2000 узлах;
- ход разреза через всё окно — около 9 мкс на 1950 связях;
- перерисовка миникарты — 1,36 мс на 2000 узлах, около половины кадра перетаскивания.

Виртуализации узлов пока нет: следующий этап — раскладка и виртуализация.

## Ограничения

- Перестановка, которая заново раскладывает элемент между портом и узлом, не трогая ни того, ни
  другого, конец связи не пересчитывает.
- Автопереворота касательных у перевалки, как в Blueprint, нет.

Рабочий хост со всеми запросами, перевалкой, разрезом, миникартой и отменой — `samples/Nodes.Demo`.
