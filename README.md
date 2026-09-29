# ArxisStudio.Surface

Семейство библиотек для Avalonia 12, на котором строятся визуальные редакторы с бесконечным холстом:
дизайнеры интерфейса, редакторы узлов, схем и макетов. Четыре сборки, четыре пакета, ссылки только
вниз: ядро не знает, что лежит на холсте, инструменты не знают редакторов, а два редактора — друг
друга.

![Дизайнер интерфейса](docs/images/overview.png)

## Пакеты

| Пакет | Что даёт | Зависит от | Документация |
| --- | --- | --- | --- |
| `ArxisStudio.Surface` | холст, viewport, сетка, контейнеры, выделение, жесты, клавиатура, контракт изменений, `SurfaceHistory` | Avalonia | [src/Surface](src/Surface/README.md) |
| `ArxisStudio.Surface.Editing` | ручки изменения размера, привязка, направляющие и интервалы, линейки, блокировки, миникарта | ядро | [src/Surface.Editing](src/Surface.Editing/README.md) |
| `ArxisStudio.Surface.UiDesigner` | `UiDesignerView`: дизайнер интерфейса — вложенные контролы, раскладка, перестановка, группы | ядро, инструменты | [src/Surface.UiDesigner](src/Surface.UiDesigner/README.md) |
| `ArxisStudio.Surface.Nodes` | `NodeEditor`: узлы, порты, связи, перевалки, разрез | ядро, инструменты | [src/Surface.Nodes](src/Surface.Nodes/README.md) |

Все четыре — `net8.0` (минимальный TFM Avalonia 12), Avalonia 12.1.1, версия `0.x`: публичная
поверхность закреплена слепками, но ещё меняется. В NuGet библиотека не публикуется; подключают её
ссылкой на проект или локальным пакетом (`dotnet pack`).

## С чего начать

Выбор — какой редактор нужен. Каждая тема ниже включает темы нижних слоёв, приложение подключает одну.

| Нужно | Контрол | Тема |
| --- | --- | --- |
| свой редактор поверх холста | `SurfaceView` | `avares://ArxisStudio.Surface/Themes/SurfaceTheme.axaml` |
| редактировать интерфейс Avalonia | `UiDesignerView` | `avares://ArxisStudio.Surface.UiDesigner/Themes/UiDesignerTheme.axaml` |
| граф узлов | `NodeEditor` | `avares://ArxisStudio.Surface.Nodes/Themes/ArxisStudioNodeEditorTheme.axaml` |

Все сборки объявлены под одним адресом разметки:

```xml
xmlns:surface="https://github.com/Arxis-Team/ArxisStudio.Surface"
```

```xml
<surface:UiDesignerView ItemsSource="{Binding Screens}" SelectionMode="Multiple" />

<surface:NodeEditor ItemsSource="{Binding Nodes}" ItemHeaderBinding="{Binding Title}"
                    Links="{Binding Links}"
                    LinkSourceBinding="{Binding From}" LinkTargetBinding="{Binding To}" />
```

## Принципы

- **Поверхность правит геометрию и не правит структуру** ([ADR 0001](docs/adr/0001-the-editor-reads-the-tree-and-never-writes-it.md)).
  Положение, размер и `ZIndex` она пишет сама и сообщает о них `EditCompleted`; создание, удаление,
  перестановку и связи она просит у хоста запросами с `Handled`.
- **Отмену ведёт хост.** Готовая история — `SurfaceHistory`: единицы редактирования поверхности и
  структурные правки хоста (`ISurfaceChange`) в одном стеке.
- **Место хранения пометок — за владельцем документа** ([ADR 0002](docs/adr/0002-where-the-group-mark-lives-belongs-to-the-document-owner.md)):
  группы дизайнера интерфейса пишутся через сменное хранилище.
- **Ссылки только вниз** ([ADR 0003](docs/adr/0003-the-library-is-three-layers-and-references-go-down.md),
  [0004](docs/adr/0004-the-node-editor-is-a-fourth-layer-beside-the-form-designer.md)). Направление
  между сборками держит компилятор, внутри сборки и в тестах — `LayerDependencyTests`. Швы между
  слоями internal и открыты слоям выше через `InternalsVisibleTo`.
- **Имена следуют слоям** ([ADR 0006](docs/adr/0006-names-follow-the-layers.md)): приставка `Surface`,
  главный контрол слоя — `<Слой>View`. Там же таблица соответствия прежним именам `Design*`.
- **Контейнеры следуют видимой области** ([ADR 0007](docs/adr/0007-containers-follow-the-viewport.md)):
  с `ItemLocationBinding` контейнер есть только у видимого с запасом, геометрию свёрнутого держит
  панель, а связи редактора узлов — записи; граф на 10 000 узлов развёртывает столько же, сколько на
  2000.
- **Малый масштаб рисуется, а не разворачивается** ([ADR 0008](docs/adr/0008-the-small-zoom-is-drawn-not-realized.md)):
  ниже `SimplifiedZoom` элементы — карточки с полосой заголовка из `ItemAccentBinding`, контейнер есть
  только у закреплённого, а нажатие разворачивает карточку под собой.
- **Базовый вид — у библиотеки, дизайн — у хоста** ([ADR 0009](docs/adr/0009-the-node-card-has-a-header-and-the-host-designs-it.md)):
  без строки оформления узел — карточка с названием на цветной полосе, порт — штырёк, провод — кривая;
  каждую часть хост заменяет своей темой, шаблоном или ключом, а цвета полосы и провода даёт
  привязками к модели.
- **Выбор — данные, контейнер — вид** ([ADR 0010](docs/adr/0010-selection-is-data.md)): выбранный
  элемент за окном остаётся выбранным без контейнера, «выбрать всё» на 10 000 узлах ничего не
  разворачивает, а перетаскивание и отмена двигают свёрнутое записью в модель.
- **Панорама стоит видимого** ([ADR 0011](docs/adr/0011-panning-costs-what-is-visible.md)): большие слои
  рисуются своей операцией и только видимое, миникарта кэширует содержимое, окно разворачивания
  пересматривается шагом, а запас разворачивается порциями — на 10 000 узлах панорама средней кнопкой
  на 100 % и 30 % идёт в 60 кадров.
- **Большие слои рисуют Skia напрямую** ([ADR 0012](docs/adr/0012-large-layers-draw-through-skia.md)):
  карточки — треугольниками, связи — путём на перо, миникарта — своей картинкой; «показать всё» на
  10 000 узлах — тоже 60 кадров. Без Skia — прежний путь; ядро зависит от `Avalonia.Skia`.
- **Значения — ресурсами.** Цвета, толщины, размеры ручек задаются ключами темы (`Surface.*`,
  `UiDesigner.*`, `NodeEditor.*`, `SurfaceMinimap.*`) отдельно для `Light` и `Dark`, без копирования
  шаблонов.

## Публичная поверхность

Сборки экспортируют 83 типа; всё остальное — реализация. Слепок каждой сборки — до отдельного члена и
значения по умолчанию у `AvaloniaProperty` — лежит в `tests/ArxisStudio.Surface.Tests/PublicSurface.<сборка>.baseline.txt`,
и новый публичный член роняет тест, пока его не внесут в слепок осознанно. Машины состояний, стратегии
размещения, резолверы направляющих, швы ядра и службы инструментов — internal намеренно.

## Структура репозитория

```
src/Surface/              ArxisStudio.Surface — ядро
src/Surface.Editing/      ArxisStudio.Surface.Editing — инструменты
src/Surface.UiDesigner/   ArxisStudio.Surface.UiDesigner — дизайнер интерфейса
src/Surface.Nodes/        ArxisStudio.Surface.Nodes — редактор узлов
tests/                    headless-тесты всех четырёх сборок
samples/UiDesigner.Demo/  демо дизайнера интерфейса
samples/Nodes.Demo/       демо редактора узлов
samples/Nodes.StateMachine/ машина состояний на узлах: ведёт интерфейс Avalonia, запуск, меню
docs/adr/                 архитектурные решения
```

## Сборка, тесты, демо

```bash
dotnet build ArxisStudio.Surface.sln
dotnet test tests/ArxisStudio.Surface.Tests
dotnet run --project samples/UiDesigner.Demo
dotnet run --project samples/Nodes.Demo
dotnet run --project samples/Nodes.StateMachine
dotnet pack ArxisStudio.Surface.sln -c Release -o artifacts
```

Сборка идёт с нулём предупреждений: у библиотек включён `GenerateDocumentationFile`, и публичный член
без XML-комментария — ошибка. Тесты — headless-UI на `Avalonia.Headless.XUnit` (xunit v3).

Оба демо — хосты на одном публичном API. У каждого есть канал автоматизации `--automation <каталог>`:
команды и ответы файлами, с точными числами состояния редактора, — и в отладочной сборке F12
открывает инструменты разработчика.

## Решения

| ADR | Решение |
| --- | --- |
| [0001](docs/adr/0001-the-editor-reads-the-tree-and-never-writes-it.md) | Редактор владеет геометрией; структуру он читает и выражает запросами |
| [0002](docs/adr/0002-where-the-group-mark-lives-belongs-to-the-document-owner.md) | Смысл группы — за редактором, место её хранения — за владельцем документа |
| [0003](docs/adr/0003-the-library-is-three-layers-and-references-go-down.md) | Библиотека — слои, ссылки идут только вниз |
| [0004](docs/adr/0004-the-node-editor-is-a-fourth-layer-beside-the-form-designer.md) | Редактор узлов — четвёртый слой рядом с дизайнером интерфейса; связь знает концы данными портов |
| [0005](docs/adr/0005-a-bend-is-a-reroute-node-and-the-minimap-is-a-surface-tool.md) | Излом связи — узел-перевалка, разрез — удаление перечёркнутых, миникарта — инструмент любой поверхности |
| [0006](docs/adr/0006-names-follow-the-layers.md) | Имена следуют слоям: приставка `Design` уступает `Surface`, дизайнер — `UiDesignerView` |
| [0007](docs/adr/0007-containers-follow-the-viewport.md) | Контейнеры следуют видимой области: раскладка по изменениям, виртуализация узлов и связей, закешированная миникарта |
| [0008](docs/adr/0008-the-small-zoom-is-drawn-not-realized.md) | Малый масштаб рисуется: карточки без контейнеров с полосой заголовка из привязки хоста, нажатие разворачивает |
| [0009](docs/adr/0009-the-node-card-has-a-header-and-the-host-designs-it.md) | У карточки узла есть заголовок, а дизайн задаёт хост: базовый вид — темами, цвета полосы и провода — привязками к модели |
| [0010](docs/adr/0010-selection-is-data.md) | Выбор — данные, контейнер — вид: полный выбор — элементы, свёрнутое двигается через модель |
| [0011](docs/adr/0011-panning-costs-what-is-visible.md) | Панорама стоит видимого: слои — своей операцией по сетке ячеек, миникарта — картинкой, окно — шагом, запас — порциями |
| [0012](docs/adr/0012-large-layers-draw-through-skia.md) | Большие слои рисуют Skia напрямую: карточки — треугольниками, связи — путём на перо, миникарта — своей картинкой; без Skia — прежний путь |

## Лицензия

MIT, см. [LICENSE](LICENSE).
