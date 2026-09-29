using System;
using Avalonia.Media;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Импульс по проводу — отладка потока, как пузыри на проводах исполнения в Blueprint (ADR 0014).
/// </summary>
/// <remarks>
/// Хост зовёт <see cref="NodeEditor.PulseLink(object, LinkPulse?)"/>, когда провод сработал: провод
/// вспыхивает свечением, и по нему от выхода ко входу бегут фигуры — <see cref="Speed"/> мировых единиц
/// в секунду, через <see cref="Spacing"/>. Импульс живёт <see cref="Lifetime"/> и гаснет за
/// <see cref="FadeOut"/>; новый вызов по той же связи продлевает его, не сбивая бег фигур, — провод,
/// который срабатывает на каждом кадре, горит ровно, а не мигает. Анимация конечна: бесконечного бега
/// нет, провод, переставший срабатывать, гаснет сам.
/// <para>
/// Кисти и размер без значения берутся из темы — ключи <c>NodeEditor.LinkPulse.*</c>; задают их здесь,
/// когда импульсу одной связи нужен свой вид. Объект читается на каждом кадре; импульсы целиком
/// выключает <see cref="NodeEditor.IsLinkPulseEnabled"/>.
/// </para>
/// </remarks>
public sealed class LinkPulse
{
    /// <summary>
    /// Получает или задает фигуру импульса. По умолчанию — круг, пузырь Blueprint.
    /// </summary>
    public LinkShape Shape { get; set; } = LinkShape.Circle;

    /// <summary>
    /// Получает или задает свою фигуру для <see cref="LinkShape.Custom"/>: геометрию в квадрате от −1 до 1,
    /// остриём по оси X.
    /// </summary>
    public Geometry? Geometry { get; set; }

    /// <summary>
    /// Получает или задает размер фигуры в мировых единицах — её длину по проводу. Без значения — ключ
    /// темы <c>NodeEditor.LinkPulse.Size</c>.
    /// </summary>
    public double? Size { get; set; }

    /// <summary>
    /// Получает или задает скорость фигур в мировых единицах в секунду. По умолчанию 192, как у Blueprint.
    /// </summary>
    public double Speed { get; set; } = 192;

    /// <summary>
    /// Получает или задает шаг между фигурами по проводу в мировых единицах. По умолчанию 64.
    /// </summary>
    public double Spacing { get; set; } = 64;

    /// <summary>
    /// Получает или задает, сколько импульс горит в полную силу после последнего вызова. По умолчанию
    /// секунда.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Получает или задает время разгорания. По умолчанию 80 мс: вспышка видна сразу, но не щёлкает.
    /// </summary>
    public TimeSpan FadeIn { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Получает или задает время угасания. По умолчанию 400 мс.
    /// </summary>
    public TimeSpan FadeOut { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Получает или задает кисть фигур. Без значения — ключ темы <c>NodeEditor.LinkPulse.Brush</c>.
    /// </summary>
    public IBrush? Brush { get; set; }

    /// <summary>
    /// Получает или задает кисть свечения провода. Без значения — ключ темы
    /// <c>NodeEditor.LinkPulse.GlowBrush</c>.
    /// </summary>
    public IBrush? GlowBrush { get; set; }

    /// <summary>
    /// Получает или задает толщину свечения в мировых единицах; ноль выключает свечение. Без значения —
    /// ключ темы <c>NodeEditor.LinkPulse.GlowThickness</c>.
    /// </summary>
    public double? GlowThickness { get; set; }

    /// <summary>
    /// Сила импульса в момент <paramref name="age"/> после последнего вызова: от 0 до 1.
    /// </summary>
    internal double Envelope(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            return 0;

        if (age < FadeIn)
            return age / FadeIn;

        if (age < Lifetime)
            return 1;

        var fading = age - Lifetime;
        return fading < FadeOut && FadeOut > TimeSpan.Zero ? 1 - (fading / FadeOut) : 0;
    }

    /// <summary>
    /// Погас ли импульс совсем.
    /// </summary>
    internal bool IsOver(TimeSpan age) => age >= Lifetime + FadeOut && age >= FadeIn;
}
