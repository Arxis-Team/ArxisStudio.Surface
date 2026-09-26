using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Отвечает, что внутри контейнера может стать target'ом выделения и жеста.
/// </summary>
/// <remarks>
/// Ядро не знает, что лежит в контейнере (ADR 0003): форма Avalonia, узел графа или
/// фигура. Остальные правила выделения — target по умолчанию, попадание точкой,
/// редактируемость target'а, принесённого снаружи, — ядро выводит из этих двух ответов.
/// </remarks>
internal interface ISurfaceTargetResolver
{
    /// <summary>
    /// Перечисляет кандидатов в target'ы внутри контейнера, не включая его самого.
    /// </summary>
    IEnumerable<Control> EnumerateCandidates(SurfaceItem host);

    /// <summary>
    /// Может ли кандидат стать target'ом внутри контейнера.
    /// </summary>
    bool IsSelectable(Control target, SurfaceItem host);
}

/// <summary>
/// Резолвер ядра: target'ом бывает только контейнер целиком.
/// </summary>
internal sealed class ContainerTargetResolver : ISurfaceTargetResolver
{
    public static readonly ContainerTargetResolver Instance = new();

    private ContainerTargetResolver()
    {
    }

    public IEnumerable<Control> EnumerateCandidates(SurfaceItem host) => Array.Empty<Control>();

    public bool IsSelectable(Control target, SurfaceItem host) => false;
}
