using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// То, что пользователь видит в выделении одной рамкой.
/// </summary>
/// <remarks>
/// Кластер — это либо группа целиком на видимом сейчас уровне, либо одиночный контрол.
/// Понятие заведено потому, что выделение перестало быть плоским: выбрав группу и добавив
/// соседа, пользователь ждёт две рамки, а не столько, сколько контролов внутри.
/// <para>
/// Группа становится кластером, только если выбраны <b>все</b> её участники. Иначе рамка
/// обещала бы жест над тем, чего в выделении нет, и её участники идут отдельными кластерами.
/// </para>
/// </remarks>
internal sealed class SelectionCluster
{
    private SelectionCluster(UiDesignerItem host, string? groupPath, IReadOnlyList<Control> members)
    {
        Host = host;
        GroupPath = groupPath;
        Members = members;
    }

    /// <summary>Форма, которой принадлежит кластер: путь группы осмыслен только внутри неё.</summary>
    public UiDesignerItem Host { get; }

    /// <summary>Путь группы или <see langword="null"/> у одиночного контрола.</summary>
    public string? GroupPath { get; }

    /// <summary>Состав кластера в порядке выделения.</summary>
    public IReadOnlyList<Control> Members { get; }

    /// <summary>Признак кластера-группы.</summary>
    public bool IsGroup => GroupPath != null;

    /// <summary>Первый участник: по нему кластер адресуется в оверлее.</summary>
    public Control Primary => Members[0];

    /// <summary>Создаёт кластер из одиночного контрола.</summary>
    public static SelectionCluster Single(UiDesignerItem host, Control target) => new(host, null, new[] { target });

    /// <summary>Создаёт кластер из группы.</summary>
    public static SelectionCluster Group(UiDesignerItem host, string path, IReadOnlyList<Control> members)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        return new SelectionCluster(host, path, members);
    }
}
