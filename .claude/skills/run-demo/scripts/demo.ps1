<#
.SYNOPSIS
Запускает UiDesigner.Demo и управляет им для визуальной проверки правок.

.DESCRIPTION
Обвязка над Win32 для сценария "собрал -> посмотрел -> потыкал -> закрыл".
Процесс ищется по имени, поэтому состояние между вызовами не нужно.

ВАЖНО: координаты click/wheel задаются ОТНОСИТЕЛЬНО ОКНА (как на скриншоте),
скрипт сам переводит их в экранные. Скриншот снимается по прямоугольнику окна,
поэтому пиксель (x, y) на картинке -> те же (x, y) в параметрах.

.EXAMPLE
demo.ps1 -Action start
demo.ps1 -Action shot  -Out C:\tmp\1.png
demo.ps1 -Action click -X 706 -Y 453
demo.ps1 -Action doubleclick -X 140 -Y 211
demo.ps1 -Action drag  -X 706 -Y 453 -ToX 760 -ToY 500
demo.ps1 -Action drag  -X 706 -Y 453 -ToX 760 -ToY 500 -Modifier Alt
demo.ps1 -Action dragshot -X 706 -Y 453 -ToX 760 -ToY 500 -Out C:	mp\mid.png
demo.ps1 -Action key   -Key Right -Notches 5
demo.ps1 -Action wheel -X 300 -Y 200 -Notches 4
demo.ps1 -Action stop
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('start', 'shot', 'click', 'doubleclick', 'rightclick', 'drag', 'dragshot', 'wheel', 'key', 'stop', 'status')]
    [string]$Action,

    [string]$Out,

    # Снимать с экрана вместо PrintWindow: нужно для контекстного меню,
    # которое живёт отдельным окном. Сторож откажет, если сверху чужое окно.
    [switch]$Screen,

    # Поднять MCP-эндпоинт DevTools на 127.0.0.1:5174 (только при -Action start).
    # Порт не умолчание AvaDevTools (5171): его занимает студия ArxisStudio.
    # Выключен по умолчанию: порт открывается только когда об этом просят.
    [switch]$Mcp,

    [int]$X = 0,
    [int]$Y = 0,
    [int]$ToX = 0,
    [int]$ToY = 0,
    [ValidateSet('None', 'Ctrl', 'Shift', 'Alt', 'CtrlShift')]
    [string]$Modifier = 'None',

    [ValidateSet('Left', 'Right', 'Up', 'Down', 'Delete', 'Escape', 'A', 'Z', 'Y', 'X', 'F12', 'Tab', 'Space')]
    [string]$Key = 'Right',

    [int]$Notches = 3,
    [int]$Repeat = 1,
    [int]$TimeoutSec = 20
)

$ErrorActionPreference = 'Stop'
$ProcessName = 'UiDesigner.Demo'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class DemoDriver
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT val, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    private static byte Vk(string modifier)
    {
        if (modifier == "Ctrl") return 0x11;
        if (modifier == "Shift") return 0x10;
        if (modifier == "Alt") return 0x12;
        return 0;
    }

    private static void ModifierDown(string modifier)
    {
        if (modifier == "CtrlShift")
        {
            keybd_event(0x11, 0, 0, IntPtr.Zero);
            keybd_event(0x10, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(60);
            return;
        }

        byte vk = Vk(modifier);
        if (vk != 0) { keybd_event(vk, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); }
    }

    private static void ModifierUp(string modifier)
    {
        if (modifier == "CtrlShift")
        {
            keybd_event(0x10, 0, 0x0002, IntPtr.Zero);
            keybd_event(0x11, 0, 0x0002, IntPtr.Zero);
            System.Threading.Thread.Sleep(60);
            return;
        }

        byte vk = Vk(modifier);
        if (vk != 0) { keybd_event(vk, 0, 0x0002, IntPtr.Zero); System.Threading.Thread.Sleep(60); }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }

    // SetForegroundWindow молча не срабатывает, если вызывающий процесс сам не на
    // переднем плане: Windows это ограничивает. Раньше отказ был не виден, и снимок
    // брал то окно, что оказалось в координатах демо, — вплоть до чужого и личного.
    // Поэтому результат проверяется, а не предполагается.
    public static void Focus(IntPtr h)
    {
        for (int i = 0; i < 5; i++)
        {
            // Права на вывод вперёд есть только у процесса, который уже на переднем
            // плане. Штатный обход — на время присоединить свой ввод к его потоку.
            uint mine = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint theirs = GetWindowThreadProcessId(h, IntPtr.Zero);

            AttachThreadInput(mine, theirs, true);
            ShowWindow(h, 5);   // SW_SHOW: не трогает развёрнутость окна
            SetForegroundWindow(h);
            AttachThreadInput(mine, theirs, false);

            System.Threading.Thread.Sleep(600);
            if (GetForegroundWindow() == h) return;
        }

        throw new InvalidOperationException(
            "Окно демо не удалось вывести на передний план: снимок захватил бы чужое окно.");
    }

    // Клавиши идут тем же низкоуровневым путём, что и мышь. SendKeys здесь не работал:
    // замер показывал, что Ctrl + A не меняет выделение, то есть нажатие до приложения
    // не доходило вовсе.
    public static void Key(IntPtr h, byte vk, string modifier)
    {
        Focus(h);
        byte[] mods = Modifiers(modifier);

        foreach (byte m in mods) keybd_event(m, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        keybd_event(vk, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        keybd_event(vk, 0, 2, IntPtr.Zero);
        for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, IntPtr.Zero);
        System.Threading.Thread.Sleep(400);
    }

    static byte[] Modifiers(string modifier)
    {
        switch (modifier)
        {
            case "Ctrl": return new byte[] { 0x11 };
            case "Shift": return new byte[] { 0x10 };
            case "Alt": return new byte[] { 0x12 };
            case "CtrlShift": return new byte[] { 0x11, 0x10 };
            default: return new byte[0];
        }
    }

    // Снимок берётся у самого окна (PrintWindow), а не с экрана. CopyFromScreen
    // копирует пиксели по координатам, то есть всё, что лежит сверху: всплывающее
    // уведомление чужого приложения попадает в файл и уезжает в репозиторий.
    // Это уже случилось один раз.
    public static void Shot(IntPtr h, string path)
    {
        RECT r = Rect(h);
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        using (var bmp = new Bitmap(w, ht))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                try
                {
                    // PW_RENDERFULLCONTENT: без него композиторный рендер даёт пустоту.
                    if (!PrintWindow(h, dc, 2))
                        throw new InvalidOperationException("PrintWindow не смог снять окно.");
                }
                finally { g.ReleaseHdc(dc); }
            }

            // Пустой кадр PrintWindow возвращает молча, признаком служит содержимое.
            if (IsBlank(bmp))
                throw new InvalidOperationException(
                    "PrintWindow вернул пустой кадр: снимок не сохранён.");

            bmp.Save(path, ImageFormat.Png);
        }
    }

    // Снимок с экрана. Нужен только там, где содержимое лежит в отдельном окне —
    // контекстное меню Avalonia это popup, и PrintWindow главного окна его не видит.
    // Ценой идёт риск захватить чужое окно, поэтому перед копированием проверяется
    // Z-порядок: всё, что лежит выше демо и перекрывает её, снимок отменяет.
    public static void ShotScreen(IntPtr h, string path)
    {
        RECT r = Rect(h);
        Intruder(h, r);

        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        using (var bmp = new Bitmap(w, ht))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, ht));
            bmp.Save(path, ImageFormat.Png);
        }
    }

    // Видимые границы окна. У максимизированного окна рамка шире нарисованного:
    // Windows держит по краям невидимую полосу для растягивания, и панель задач
    // с ней пересекается всегда. Сторож, сравнивающий рамки, отказывал бы каждый раз.
    private static RECT Visible(IntPtr h)
    {
        RECT frame;
        // DWMWA_EXTENDED_FRAME_BOUNDS = 9
        if (DwmGetWindowAttribute(h, 9, out frame, Marshal.SizeOf(typeof(RECT))) == 0)
            return frame;
        return Rect(h);
    }

    private static void Intruder(IntPtr h, RECT windowRect)
    {
        RECT area = Visible(h);
        uint owner;
        GetWindowThreadProcessId(h, out owner);

        // GW_HWNDPREV = 3: шаг вверх по Z-порядку.
        for (IntPtr top = GetWindow(h, 3); top != IntPtr.Zero; top = GetWindow(top, 3))
        {
            if (!IsWindowVisible(top))
                continue;

            // DWMWA_CLOAKED = 14: окно другого рабочего стола или UWP-заготовка.
            int cloaked;
            if (DwmGetWindowAttribute(top, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                continue;

            uint pid;
            GetWindowThreadProcessId(top, out pid);
            if (pid == owner)
                continue;   // popup самой демо — его и снимаем

            RECT o = Visible(top);
            if (o.Right <= area.Left || o.Left >= area.Right || o.Bottom <= area.Top || o.Top >= area.Bottom)
                continue;

            throw new InvalidOperationException(
                "Над окном демо чужое окно (pid " + pid + "): снимок захватил бы его содержимое.");
        }
    }

    private static bool IsBlank(Bitmap bmp)
    {
        Color first = bmp.GetPixel(0, 0);
        for (int x = 0; x < bmp.Width; x += 17)
            for (int y = 0; y < bmp.Height; y += 17)
                if (bmp.GetPixel(x, y) != first)
                    return false;
        return true;
    }

    // wx/wy - координаты относительно окна
    public static void Click(IntPtr h, int wx, int wy, string modifier)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        ModifierDown(modifier);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        ModifierUp(modifier);
        System.Threading.Thread.Sleep(900);
    }

    // Двойной клик: вход в группу у редактора и правка имени в панели слоёв.
    // Пауза между нажатиями заведомо меньше системного порога двойного клика,
    // поэтому два отдельных вызова Click его не заменяют - там 900 мс на выходе.
    public static void DoubleClick(IntPtr h, int wx, int wy)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);

        for (int i = 0; i < 2; i++)
        {
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(40);
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
            if (i == 0) System.Threading.Thread.Sleep(60);
        }

        System.Threading.Thread.Sleep(900);
    }

    // Правый клик: контекстное меню редактора открывается по RMB.
    public static void RightClick(IntPtr h, int wx, int wy)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        mouse_event(0x0008, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0010, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(900);
    }

    // Перетаскивание. Промежуточные шаги обязательны: один прыжок из точки в
    // точку не переводит контейнер в состояние drag - редактору нужен сдвиг,
    // превышающий DragStartThreshold, а затем сами move-события.
    public static void Drag(IntPtr h, int wx, int wy, int tx, int ty, string modifier)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        ModifierDown(modifier);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(150);

        const int steps = 12;
        for (int i = 1; i <= steps; i++)
        {
            int x = wx + ((tx - wx) * i / steps);
            int y = wy + ((ty - wy) * i / steps);
            SetCursorPos(r.Left + x, r.Top + y);
            System.Threading.Thread.Sleep(40);
        }

        System.Threading.Thread.Sleep(150);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        ModifierUp(modifier);
        System.Threading.Thread.Sleep(900);
    }

    // Снимок в середине жеста, до отпускания кнопки. Нужен для оверлеев,
    // которые живут только внутри жеста: направляющие, marquee, индикатор
    // вставки. Обычный drag их не покажет - к моменту скриншота они убраны.
    public static void DragShot(IntPtr h, int wx, int wy, int tx, int ty, string modifier, string path)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        ModifierDown(modifier);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(150);

        const int steps = 12;
        for (int i = 1; i <= steps; i++)
        {
            int x = wx + ((tx - wx) * i / steps);
            int y = wy + ((ty - wy) * i / steps);
            SetCursorPos(r.Left + x, r.Top + y);
            System.Threading.Thread.Sleep(40);
        }

        System.Threading.Thread.Sleep(400);
        Shot(h, path);

        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        ModifierUp(modifier);
        System.Threading.Thread.Sleep(900);
    }

    public static void Wheel(IntPtr h, int wx, int wy, int notches)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(200);
        uint delta = (uint)(notches > 0 ? 120 : -120);
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            mouse_event(0x0800, 0, 0, delta, IntPtr.Zero);
            System.Threading.Thread.Sleep(120);
        }
        System.Threading.Thread.Sleep(800);
    }
}
'@

function Get-Demo {
    $p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $p) { return $null }
    $p.Refresh()
    return $p
}

function Require-Demo {
    $p = Get-Demo
    if ($null -eq $p) { throw "Демо не запущено. Сначала: demo.ps1 -Action start" }
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw "У процесса ещё нет окна." }
    return $p
}

switch ($Action) {
    'start' {
        $existing = Get-Demo
        if ($null -ne $existing) { "already running pid=$($existing.Id)"; break }

        $root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
        $exe = Join-Path $root 'samples\UiDesigner.Demo\bin\Debug\net10.0\UiDesigner.Demo.exe'
        if (-not (Test-Path $exe)) { throw "Не найден $exe - сначала соберите проект (dotnet build)." }

        $logDir = Join-Path $env:TEMP 'uidesigner-demo'
        if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

        # Переменную читает сам AvaDevTools; дочерний процесс наследует её.
        # Только чтение: ввод и заморозка требуют отдельных AVA_DEVTOOLS_MCP_INPUT
        # и AVA_DEVTOOLS_MCP_HOLD, и включать их по умолчанию нельзя — клик из
        # агента выполняет обработчики приложения.
        # Порт задаётся явно: умолчание AvaDevTools (5171) — порт студии ArxisStudio,
        # и демо рядом с ней не поднялось бы или отвечало бы вместо неё.
        if ($Mcp) {
            $env:AVA_DEVTOOLS_MCP = '1'
            $env:AVA_DEVTOOLS_MCP_PORT = '5174'
        } else {
            Remove-Item Env:\AVA_DEVTOOLS_MCP -ErrorAction SilentlyContinue
            Remove-Item Env:\AVA_DEVTOOLS_MCP_PORT -ErrorAction SilentlyContinue
        }

        $p = Start-Process -FilePath $exe -PassThru `
             -RedirectStandardOutput (Join-Path $logDir 'out.log') `
             -RedirectStandardError  (Join-Path $logDir 'err.log')

        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        while ((Get-Date) -lt $deadline) {
            if ($p.HasExited) {
                $err = Get-Content (Join-Path $logDir 'err.log') -Raw -ErrorAction SilentlyContinue
                throw "Процесс завершился с кодом $($p.ExitCode). stderr:`n$err"
            }
            $p.Refresh()
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
                $mcpNote = if ($Mcp) { " mcp=http://127.0.0.1:5174/" } else { "" }
                "started pid=$($p.Id) window='$($p.MainWindowTitle)' logs=$logDir$mcpNote"
                break
            }
            Start-Sleep -Milliseconds 400
        }
        if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw "Окно не появилось за $TimeoutSec с." }
    }

    'status' {
        $p = Get-Demo
        if ($null -eq $p) { 'not running'; break }
        $r = [DemoDriver]::Rect($p.MainWindowHandle)
        "pid=$($p.Id) responding=$($p.Responding) title='$($p.MainWindowTitle)' rect=$($r.Left),$($r.Top) size=$($r.Right-$r.Left)x$($r.Bottom-$r.Top)"
    }

    'shot' {
        $p = Require-Demo
        if ([string]::IsNullOrWhiteSpace($Out)) { throw '-Out обязателен для shot.' }
        $dir = Split-Path -Parent $Out
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [DemoDriver]::Focus($p.MainWindowHandle)
        if ($Screen) { [DemoDriver]::ShotScreen($p.MainWindowHandle, $Out) }
        else { [DemoDriver]::Shot($p.MainWindowHandle, $Out) }
        "saved $Out ($((Get-Item $Out).Length) bytes)"
    }

    'click' {
        $p = Require-Demo
        [DemoDriver]::Click($p.MainWindowHandle, $X, $Y, $Modifier)
        "clicked window-relative $X,$Y modifier=$Modifier"
    }

    'doubleclick' {
        $p = Require-Demo
        [DemoDriver]::DoubleClick($p.MainWindowHandle, $X, $Y)
        "double-clicked window-relative $X,$Y"
    }

    'rightclick' {
        $p = Require-Demo
        [DemoDriver]::RightClick($p.MainWindowHandle, $X, $Y)
        "right-clicked window-relative $X,$Y"
    }

    'drag' {
        $p = Require-Demo
        [DemoDriver]::Drag($p.MainWindowHandle, $X, $Y, $ToX, $ToY, $Modifier)
        "dragged window-relative $X,$Y -> $ToX,$ToY modifier=$Modifier"
    }

    'dragshot' {
        $p = Require-Demo
        if (-not $Out) { throw "dragshot требует -Out" }
        $full = [System.IO.Path]::GetFullPath($Out)
        [DemoDriver]::DragShot($p.MainWindowHandle, $X, $Y, $ToX, $ToY, $Modifier, $full)
        "dragshot window-relative $X,$Y -> $ToX,$ToY modifier=$Modifier saved $full"
    }

    'key' {
        $p = Require-Demo

        $vk = switch ($Key) {
            'Left'   { 0x25 }
            'Right'  { 0x27 }
            'Up'     { 0x26 }
            'Down'   { 0x28 }
            'Delete' { 0x2E }
            'Escape' { 0x1B }
            'F12'    { 0x7B }   # DevTools демо
            'Tab'    { 0x09 }
            'Space'  { 0x20 }
            'A'      { 0x41 }
            'Z'      { 0x5A }
            'Y'      { 0x59 }
            'X'      { 0x58 }
        }

        for ($i = 0; $i -lt $Repeat; $i++) {
            [DemoDriver]::Key($p.MainWindowHandle, [byte]$vk, $Modifier)
        }

        "key $Key modifier=$Modifier repeat=$Repeat"
    }
    'wheel' {
        $p = Require-Demo
        [DemoDriver]::Wheel($p.MainWindowHandle, $X, $Y, $Notches)
        "wheel $Notches at window-relative $X,$Y"
    }

    'stop' {
        $p = Get-Demo
        if ($null -eq $p) { 'not running'; break }
        Stop-Process -Id $p.Id -Force
        Start-Sleep -Milliseconds 500
        "stopped pid=$($p.Id)"
    }
}
