namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Форма штырька порта (ADR 0016).
/// </summary>
/// <remarks>
/// Форма отличает виды портов, как в Blueprint: пин выполнения — пятиугольник остриём вправо, пин
/// данных — круг. Какой порт каким считать, решает хост; редактор рисует форму и больше ничего с ней не
/// связывает. Своя форма — <see cref="Custom"/> с <see cref="Port.PinGeometry"/>.
/// </remarks>
public enum PortShape
{
    /// <summary>Круг — порт данных.</summary>
    Circle,

    /// <summary>Пятиугольник остриём вправо — порт выполнения, как Exec-пин Blueprint.</summary>
    Execution,

    /// <summary>Квадрат.</summary>
    Square,

    /// <summary>Ромб.</summary>
    Diamond,

    /// <summary>Треугольник остриём вправо.</summary>
    Triangle,

    /// <summary>Геометрия хоста, <see cref="Port.PinGeometry"/>; вписывается в штырёк с сохранением пропорций.</summary>
    Custom
}
