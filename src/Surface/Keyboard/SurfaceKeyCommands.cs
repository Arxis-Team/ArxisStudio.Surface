using System;
using System.Collections;
using System.Collections.Generic;

namespace ArxisStudio.Surface;

/// <summary>
/// Упорядоченный набор клавиатурных команд поверхности.
/// </summary>
/// <remarks>
/// Нажатие проходит команды по порядку. Обрабатывает его первая, которая его узнала
/// <b>и</b> выполнилась; отказавшаяся уступает следующей. Так команда приложения на
/// ту же клавишу, что и встроенная, не отбирает у неё нажатие, когда ей делать нечего.
/// <para>
/// Встроенные команды носят идентификаторы из констант этого класса. Команда с уже
/// занятым идентификатором <b>заменяет</b> прежнюю на её месте — так меняют поведение
/// встроенной, не трогая порядка, — а <see cref="Remove"/> выключает её совсем.
/// </para>
/// </remarks>
public sealed class SurfaceKeyCommands : IReadOnlyList<SurfaceKeyCommand>
{
    /// <summary>Отмена: поднимает <see cref="SurfaceView.UndoRequested"/>.</summary>
    public const string Undo = "surface.undo";

    /// <summary>Повтор: поднимает <see cref="SurfaceView.RedoRequested"/>.</summary>
    public const string Redo = "surface.redo";

    /// <summary>
    /// Изменение размера выделения стрелками с
    /// <see cref="SurfaceInputGestures.KeyboardResizeModifiers"/> — клавиатурная замена ручкам.
    /// </summary>
    public const string Resize = "surface.resize";

    /// <summary>Смещение выделения стрелками.</summary>
    public const string Nudge = "surface.nudge";

    /// <summary>Снятие выделения по Escape.</summary>
    public const string ClearSelection = "surface.clearSelection";

    /// <summary>Запрос удаления: поднимает <see cref="SurfaceView.DeleteRequested"/>.</summary>
    public const string Delete = "surface.delete";

    /// <summary>Выбор всех контейнеров.</summary>
    public const string SelectAll = "surface.selectAll";

    private readonly List<SurfaceKeyCommand> _commands = new();

    /// <inheritdoc />
    public int Count => _commands.Count;

    /// <inheritdoc />
    public SurfaceKeyCommand this[int index] => _commands[index];

    /// <summary>
    /// Добавляет команду в конец набора или заменяет команду с тем же идентификатором на её месте.
    /// </summary>
    /// <param name="command">Команда.</param>
    public void Add(SurfaceKeyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var index = IndexOf(command.Id);
        if (index >= 0)
            _commands[index] = command;
        else
            _commands.Add(command);
    }

    /// <summary>
    /// Ставит команду на указанное место; команда с тем же идентификатором прежде снимается.
    /// </summary>
    /// <param name="index">Место в порядке обхода, от нуля до <see cref="Count"/>.</param>
    /// <param name="command">Команда.</param>
    /// <remarks>
    /// Нужна, когда команда приложения должна услышать нажатие раньше встроенной.
    /// </remarks>
    public void Insert(int index, SurfaceKeyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = IndexOf(command.Id);
        if (existing >= 0)
        {
            _commands.RemoveAt(existing);
            if (existing < index)
                index--;
        }

        if (index < 0 || index > _commands.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _commands.Insert(index, command);
    }

    /// <summary>
    /// Снимает команду.
    /// </summary>
    /// <param name="id">Идентификатор команды.</param>
    /// <returns><see langword="true"/>, если команда была в наборе.</returns>
    public bool Remove(string id)
    {
        var index = IndexOf(id);
        if (index < 0)
            return false;

        _commands.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Находит команду по идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор команды.</param>
    /// <returns>Команду или <see langword="null"/>.</returns>
    public SurfaceKeyCommand? Find(string id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : _commands[index];
    }

    /// <inheritdoc />
    public IEnumerator<SurfaceKeyCommand> GetEnumerator() => _commands.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Снимок набора для обхода: команда вправе поменять набор, пока её исполняют.
    /// </summary>
    internal SurfaceKeyCommand[] Snapshot() => _commands.ToArray();

    private int IndexOf(string id)
    {
        for (var i = 0; i < _commands.Count; i++)
        {
            if (string.Equals(_commands[i].Id, id, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
