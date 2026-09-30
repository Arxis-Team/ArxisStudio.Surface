---
name: run-demo
description: Собрать, запустить и подвигать UiDesigner.Demo, чтобы визуально проверить правку в редакторе. Умеет скриншот, клик и зум колесом по координатам окна. Использовать, когда нужно убедиться, что изменение работает в живом приложении — выделение, адорнеры, drag/resize, viewport, темы — а не только компилируется. Компиляции недостаточно: это UI-библиотека, большинство регрессий видно только в интерактиве.
---

# Запуск и проверка демо

Headless-тесты закрепляют поведение, но не рендер: тему, оверлеи и чёткость линий на DPI видно только в живом окне. Скилл запускает демо дизайнера интерфейса и берёт на себя Win32-обвязку ввода и снимков.

## Порядок

### 1. Собрать

```bash
dotnet build ArxisStudio.Surface.sln
```

Ожидается **0 ошибок, 0 предупреждений** (у библиотеки включён `GenerateDocumentationFile` → CS1591 на недокументированный публичный член).

Демо — конструктор форм на трёх семействах (ADR 0021), и его проект ссылается на соседние репозитории: рядом с этим должны лежать `ArxisStudio.ProjectSystem` и `ArxisStudio.Markup`. Без них сборка решения падает с `SURFDEMO01`; библиотеки собираются и без них — третьим вариантом из списка ниже.

Если падает `MSB3021`/`MSB3027` («файл используется другим процессом») на `ArxisStudio.Surface*.dll` — **это не ошибка кода**, а блокировка от `Avalonia.Designer.HostApp` (XAML-превьюер Rider). Компиляция при этом проходит, ломается только копирование. Варианты:

- закрыть вкладку превью в Rider;
- `taskkill //PID <pid> //F` — pid берётся из текста ошибки;
- проверить код в обход: `dotnet build src/Surface.UiDesigner/ArxisStudio.Surface.UiDesigner.csproj`.

### 2. Запустить

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action start -Project samples/UiDesigner.Demo/SmartHome/SmartHome.csproj -Form MainWindow.axaml
```

Ждёт появления окна и печатает pid. Если процесс упал — печатает stderr. Логи лежат в `%TEMP%\uidesigner-demo\`; шаги загрузки проекта демо пишет туда же, в `out.log`. Окно появляется раньше формы: проект демо собирает само, и форма нарисована, когда в `out.log` есть строка `surface UiDesignerFormItem measured …`.

**Без `-Project` поднимается экран приветствия**, а редактора на нём нет: ни холста, ни жестов. Проект нужен настоящий, на диске. Готовый лежит рядом с демо — `samples/UiDesigner.Demo/SmartHome`, одно окно с плитками в `Canvas`; у контролов есть имена, так что канал автоматизации адресует их (`ClimateTile`, `LightRoom`, `MorningScene`). С него сняты снимки README. Проект, который можно переписывать и собирать как угодно, даёт самопроверка демо — она пишет его в свою папку и оставляет там (`StudioCheckApp`):

```bash
samples/UiDesigner.Demo/bin/Debug/net10.0/UiDesigner.Demo.exe --verify "$P"
```

Папка должна быть пустой; прогон идёт около минуты и заканчивается строкой `CHECK VERDICT ok …`. Ключ `-Automation <каталог>` у `start` поднимает ещё и канал управления редактором (`--automation`, корневой `CLAUDE.md`), а `-Theme light` открывает демо в светлой вариации — переключателя темы скрипту не нажать.

**Вывод `start` в конвейер не направлять** (`… -Action start | head`): демо наследует канал конвейера, и тот ждёт его закрытия — команда не вернётся, пока демо живо. В файл — можно.

### 3. Снять скриншот

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action shot -Out "$TMP/demo.png"
```

Затем прочитать PNG инструментом Read — картинка видна напрямую.

Снимок берётся у **самого окна** (`PrintWindow`), а не с экрана: захват по координатам копирует и то, что лежит сверху, — всплывающее уведомление чужого приложения однажды так и попало в файл, который шёл в репозиторий. Поэтому чужие окна в кадр не попадают вовсе, даже если демо не на переднем плане.

Обратная сторона: содержимое, живущее в **отдельном окне**, `PrintWindow` не видит. У Avalonia это контекстное меню — оно popup. Для него есть `-Screen`:

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action shot -Screen -Out "$TMP/menu.png"
```

Этот режим копирует с экрана, поэтому перед копированием проверяет Z-порядок: всякое видимое чужое окно, перекрывающее демо, снимок отменяет с ошибкой. Окна самой демо (её popup'ы) пропускаются — их-то и надо снять. Без нужды `-Screen` не включать.

### 4. Потыкать

**Координаты — относительно окна, ровно как на скриншоте.** Скрипт сам добавляет позицию окна на экране. Это главная ловушка: окно почти никогда не в (0,0), и клик по «экранным» координатам уходит мимо.

```bash
# выбрать вложенный контрол (пиксель со скриншота)
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action click -X 706 -Y 453

# зум колесом: + приближает, - отдаляет
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action wheel -X 300 -Y 200 -Notches 4

# контекстное меню — оно должно открыться в точке клика
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action doubleclick -X 140 -Y 211
```

Двойной клик — отдельное действие, а не два `click` подряд: у одиночного на выходе стоит секундная пауза, и системный порог двойного клика она перекрывает. Им проверяется вход в группу у редактора.

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action rightclick -X 500 -Y 400

# перетаскивание: down в (X,Y), 12 промежуточных move, up в (ToX,ToY)
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action drag -X 951 -Y 441 -ToX 988 -ToY 464

# с модификатором: Ctrl двигает контейнер, Alt отключает привязку к сетке
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action drag -X 951 -Y 441 -ToX 988 -ToY 464 -Modifier Alt
```

Оверлеи, которые живут только внутри жеста — направляющие выравнивания, marquee, индикатор точки вставки, — обычным `drag` не поймать: к моменту скриншота кнопка уже отпущена и слой очищен. Для них есть `dragshot`: он снимает кадр **до** отпускания.

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action dragshot -X 518 -Y 290 -ToX 518 -ToY 430 -Out "$TMP/mid.png"
```

Промежуточные move в `drag` обязательны: один прыжок из точки в точку не переводит контейнер в состояние перетаскивания — редактору нужен сдвиг больше `DragStartThreshold`, а затем сами move-события.

`-Modifier` принимает `None` (по умолчанию), `Ctrl`, `Shift`, `Alt`, `CtrlShift` и работает для `click`, `drag` и `key`.

```bash
# выбор контейнера и добавление второго
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action click -X 578 -Y 650 -Modifier Ctrl
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action click -X 560 -Y 990 -Modifier CtrlShift
```

```bash
# клавиатура: стрелки, Shift+стрелки, Delete, Escape, Tab, Enter, Space, Ctrl+Z/X/Y, Ctrl+A, F12
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Right -Repeat 5
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Down -Modifier Shift -Repeat 2
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Z -Modifier Ctrl
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key F12

# проход одной клавиатурой: с экрана приветствия до открытого проекта
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Tab -Repeat 3
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Enter
```

Повтор клавиши — `-Repeat`, не `-Notches`: второе — щелчки колеса, и клавише оно ничего не говорит. На экране приветствия остановок `Tab` три — поиск, «Open» и список недавних проектов, — так что третий `Tab` встаёт на список, а `Enter` открывает строку под фокусом; `Shift + Tab` идёт назад.

Клавиатура идёт через `keybd_event`, а не `SendKeys`: последний до приложения не доходит, хотя окно и foreground — `Ctrl + A` через него не делал ничего. Мышь работает иначе, потому что `mouse_event` адресуется точкой экрана, а не фокусом.

Ключ `-Mcp` у `start` поднимает MCP-эндпоинт DevTools на `http://127.0.0.1:5174/`. Порт не умолчание AvaDevTools: 5171 занимает студия ArxisStudio, 5172 и 5173 — витрины контролов и иконок, поэтому скрипт ставит `AVA_DEVTOOLS_MCP_PORT` сам. Тогда состояние редактора можно читать значениями, а не снимать скриншотами:

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action start -Mcp
```

Без ключа порт не открывается. Сервер только читает: `click`, `type_text` и прочий ввод отвечают отказом, пока не включён `AVA_DEVTOOLS_MCP_INPUT` — мышь и клавиатуру ведёт этот скилл, MCP отвечает про состояние. Адрес прописан в `.mcp.json` репозитория.

`F12` открывает DevTools демо (`AvaDevTools`, подключены в `App.Initialize` под `#if DEBUG`). Окно инструментов — **отдельное**, и после его открытия `-Action shot` снимает уже его: `MainWindowHandle` процесса указывает на переднее окно. Чтобы вернуться к снимкам самой демо, инструменты надо закрыть.

Перед клавиатурным жестом нужно что-нибудь выделить кликом — иначе у редактора нет фокуса.

Известное ограничение обвязки: `Ctrl + A` через `SendKeys` до редактора не доходит, хотя `Escape`, `Delete` и стрелки доходят. Само поведение закрыто headless-тестом `KeyboardTests`.

`-Action status` печатает pid, responding, заголовок и прямоугольник окна.

Направляющие тонкие — одна линия в физический пиксель, — поэтому на полном скриншоте их легко не заметить. Смотреть надо увеличенный фрагмент: вырезать область через `System.Drawing` с `InterpolationMode = NearestNeighbor` и масштабом 3–6x. При обычном ресайзе однопиксельная линия размывается и по ней уже не сказать, пиксельная она или нет.

### 5. Закрыть

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action stop
```

Закрывать обязательно: живой процесс держит `ArxisStudio.Surface*.dll` и следующая сборка упадёт с той же MSB3021.

## Что проверять

Окно демо само показывает состояние редактора, и результат читается без догадок:

| Где | Что подтверждает |
|---|---|
| строка пути над холстом (`Window › StackPanel › GoButton`) | главный выбранный target и его место в документе |
| строка в «Hierarchy» | то же с другой стороны: выбор на холсте и в дереве — один |
| шапка «Inspector» | тип и имя выбранного, либо `nothing selected` |
| поля `Width` / `Height` инспектора | размер, как он записан в документе; пустое поле — размер не объявлен |
| метка над формой (`MainWindow.axaml 900 × 600 100%`) | размер формы и масштаб |
| `100%` внизу справа | `ViewportZoom` |

Точные числа — `SelectionBounds`, счётчики обоих слоёв выделения, положение viewport — отдаёт канал автоматизации командой `state`: с картинки их не снять.

Минимальный прогон после правок в редакторе:

1. приложение стартует, окно есть, форма нарисована, `err.log` пуст;
2. сетка выровнена, линейки стоят по краям холста;
3. клик по контролу формы → рамка с 8 ручками точно по контролу, путь над холстом и строка иерархии называют его;
4. колесо → масштаб меняется, сетка остаётся DPI-чёткой, рамка не разъезжается с контролом;
5. клик по пустому месту холста → выделение снимается, инспектор пишет `nothing selected`.

Пункт 4 важен отдельно: адорнеры позиционируются в мировых координатах с обратным масштабом, поэтому расхождение видно только на зуме, отличном от 100%.

## Ограничения

- Только Windows: используется `user32.dll` (`mouse_event`, `keybd_event`, `GetWindowRect`, `CopyFromScreen`).
- Скрипт двигает реальный курсор, жмёт реальные модификаторы и поднимает окно на передний план.
- У окна демо нет системной рамки, и развёрнутое оно отдаёт `PrintWindow` кадр с чёрной каймой в 8 пикселей: это невидимая полоса растягивания, а не дефект вёрстки. Координаты клика считаются от того же кадра, так что пиксель со снимка по-прежнему годится как есть.
- У **неразвёрнутого окна с системным заголовком** — это экран приветствия — точка ввода ложится ниже пикселя со снимка: замер дал сдвиг между 18 и 26 пикселями по вертикали (указатель, поставленный на одну строку, подсветил следующую под ней). У развёрнутого окна дизайнера сдвига нет. Целиться в приветствии надо с поправкой либо идти клавиатурой — `Tab` и `Enter` от координат не зависят.
- Навести указатель без нажатия можно действием `wheel` с `-Notches 0`: курсор встаёт в точку, колесо не крутится. Так снимают состояние под указателем.
- Жест меняет документ открытого проекта, а не зашитую в демо разметку, — но в памяти дизайнера: на диск он уходит с сохранением (`Ctrl + S`, у скрипта такой клавиши нет). `SmartHome` лежит под git, и после проверки его рабочее дерево должно остаться чистым; для проверок, которые сохраняют, собирают и запускают, берите проект из папки `--verify`.
