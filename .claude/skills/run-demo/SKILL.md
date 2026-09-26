---
name: run-demo
description: Собрать, запустить и подвигать DesignEditor.Demo, чтобы визуально проверить правку в редакторе. Умеет скриншот, клик и зум колесом по координатам окна. Использовать, когда нужно убедиться, что изменение работает в живом приложении — выделение, адорнеры, drag/resize, viewport, темы — а не только компилируется. Компиляции недостаточно: это UI-библиотека, большинство регрессий видно только в интерактиве.
---

# Запуск и проверка демо

Тестового проекта в решении нет, поэтому единственный способ проверить правку — запустить демо и потыкать. Этот скилл убирает ручную Win32-обвязку.

## Порядок

### 1. Собрать

```bash
dotnet build ArxisStudio.DesignEditor.sln
```

Ожидается **0 ошибок, 0 предупреждений** (у библиотеки включён `GenerateDocumentationFile` → CS1591 на недокументированный публичный член).

Если падает `MSB3021`/`MSB3027` («файл используется другим процессом») на `ArxisStudio.DesignEditor.dll` — **это не ошибка кода**, а блокировка от `Avalonia.Designer.HostApp` (XAML-превьюер Rider). Компиляция при этом проходит, ломается только копирование. Варианты:

- закрыть вкладку превью в Rider;
- `taskkill //PID <pid> //F` — pid берётся из текста ошибки;
- проверить код в обход: `dotnet build src/Surface.UiDesigner/ArxisStudio.Surface.UiDesigner.csproj`.

### 2. Запустить

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action start
```

Ждёт появления окна и печатает pid. Если процесс упал — печатает stderr. Логи лежат в `%TEMP%\designeditor-demo\`.

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

Двойной клик — отдельное действие, а не два `click` подряд: у одиночного на выходе стоит секундная пауза, и системный порог двойного клика она перекрывает. Им проверяются вход в группу у редактора и правка имени группы в панели групп.

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
# клавиатура: стрелки, Shift+стрелки, Delete, Escape, Ctrl+Z/X/Y, Ctrl+A, F12
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Right -Notches 5
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Down -Modifier Shift -Notches 2
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key Z -Modifier Ctrl
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action key -Key F12
```

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

Закрывать обязательно: живой процесс держит `ArxisStudio.DesignEditor.dll` и следующая сборка упадёт с той же MSB3021.

## Что проверять

Верхняя панель демо — готовый индикатор состояния, по ней читается результат без догадок:

| Поле | Что подтверждает |
|---|---|
| `N items` | `SelectedElements.Count` — счёт **контейнеров**, не вложенных targets |
| `Target:` | тип primary target (`TextBlock`, `DesignEditorItem`, …) |
| `Targets:` | `SelectedDesignTargetsCount` — вложенные targets |
| `X / Y / W / H` | геометрия выделения в design-координатах |
| `Viewport` / `%` | `ViewportLocation` и `ViewportZoom` |
| `Center / Fit / Center Sel / Fit Sel` | появляются только при непустом выделении |

Внутри Dashboard-элемента выводятся `DesignX / DesignY / X / Y` для `TextBlock1` — прямая проверка двусторонней синхронизации `Layout` (глобальные ↔ локальные координаты).

Минимальный прогон после правок в редакторе:

1. приложение стартует, окно есть, stderr пуст;
2. элементы отрисованы, сетка выровнена;
3. клик по заголовку внутри карточки → `1 items`, `Target: TextBlock`, адорнер с 8 хэндлами точно по контролу;
4. колесо → зум меняется, сетка остаётся DPI-чёткой, адорнер не разъезжается с контролом;
5. клик по пустому месту → выделение снимается.

Пункт 4 важен отдельно: адорнеры позиционируются в мировых координатах с обратным масштабом, поэтому расхождение видно только на зуме, отличном от 100%.

## Ограничения

- Только Windows: используется `user32.dll` (`mouse_event`, `keybd_event`, `GetWindowRect`, `CopyFromScreen`).
- Скрипт двигает реальный курсор, жмёт реальные модификаторы и поднимает окно на передний план.
- Верхняя панель показывает геометрию **контейнера** (`ActiveItem`), а не выбранного вложенного target. Позицию вложенного контрола видно только в самой карточке Dashboard — там выведены `DesignX / DesignY / X / Y` для `TextBlock1`. Для проверок, где важно точное положение вложенного элемента, тянуть надо именно его.
