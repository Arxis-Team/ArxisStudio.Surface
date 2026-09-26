using System;
using Avalonia.Controls;
using DesignGroupAttached = ArxisStudio.Surface.UiDesigner.DesignGroup;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

/// <summary>
/// Хранилище групп в attached-свойстве <see cref="DesignGroup"/>.
/// </summary>
/// <remarks>
/// Умолчание редактора и единственная реализация, которую библиотека приносит с собой: пометка
/// лежит на самом контроле, поэтому переживает всё, что переживает контрол, и пишется руками в
/// разметке. Цена записана в ADR 0002 — значение попадает в документ пользователя, а документ
/// приобретает зависимость от сборки редактора.
/// </remarks>
public sealed class DesignGroupAttachedStore : IDesignGroupStore
{
    /// <summary>
    /// Получает общий экземпляр хранилища.
    /// </summary>
    /// <remarks>
    /// Своего состояния у него нет — только чтение и запись attached-свойства, — поэтому
    /// заводить по экземпляру на редактор незачем.
    /// </remarks>
    public static DesignGroupAttachedStore Default { get; } = new DesignGroupAttachedStore();

    private DesignGroupAttachedStore()
    {
    }

    /// <inheritdoc />
    public string? GetGroup(Control target) => DesignGroupAttached.GetId(target);

    /// <inheritdoc />
    public void SetGroup(Control target, string? path) => DesignGroupAttached.SetId(target, path);

    /// <summary>
    /// Не происходит никогда: чужую запись в attached-свойство это хранилище не видит.
    /// </summary>
    /// <remarks>
    /// Это не заглушка, а честный ответ. Узнать о правке, сделанной мимо редактора, можно было
    /// бы только подписавшись на каждый контрол приложения; ровно так редактор себя и вёл до
    /// появления шва — о чужой пометке он не узнавал. Хосту, которому это нужно, дешевле
    /// подписаться на <c>DesignGroup.IdProperty.Changed</c> и поднять событие своего хранилища.
    /// </remarks>
    public event EventHandler? GroupsChanged
    {
        add { }
        remove { }
    }
}
