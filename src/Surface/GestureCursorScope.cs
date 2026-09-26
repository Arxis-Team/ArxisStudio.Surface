using Avalonia.Input;

namespace ArxisStudio.Surface;

/// <summary>
/// Курсор на время жеста: одна точка применения и одна возврата.
/// </summary>
/// <remarks>
/// Курсор ставится <b>тому, кто держит захват указателя</b>: состояния контейнера — самому
/// контейнеру, состояния редактора — редактору. Во время захвата указатель считается
/// находящимся над захватившим элементом, поэтому его курсор выигрывает у содержимого
/// формы, задавшего свой собственный, и полагаться на наследование <c>Cursor</c> не нужно.
/// <para>
/// На выходе возвращается <b>прежнее значение</b>, а не <see cref="Cursor.Default"/>:
/// иначе жест затирал бы курсор, заданный хостом. Запись идёт через
/// <c>SetCurrentValue</c>, поэтому привязка хоста к <c>Cursor</c> переживает жест —
/// тем же способом, что видимость линейки переживает <c>ShowRulers</c>.
/// </para>
/// </remarks>
internal sealed class GestureCursorScope
{
    private InputElement? _owner;
    private Cursor? _previous;
    private Cursor? _applied;

    /// <summary>
    /// Ставит курсор жеста, запомнив прежний.
    /// </summary>
    /// <param name="owner">Элемент, держащий захват указателя.</param>
    /// <param name="cursor">Курсор жеста.</param>
    public void Apply(InputElement owner, Cursor cursor)
    {
        // Тот же курсор тому же элементу — ничего не делаем. Курсор жеста ставится
        // на каждом движении указателя, и без этой проверки одна протяжка писала
        // свойство десятки раз: замерено на живом демо — 48 записей там, где нужна
        // одна. Та же дисциплина, что у снимка выделения.
        if (ReferenceEquals(_owner, owner) && ReferenceEquals(_applied, cursor))
            return;

        // Повторное применение без возврата потеряло бы исходный курсор: со второго
        // раза «прежним» стал бы курсор предыдущего жеста.
        Restore();

        _owner = owner;
        _previous = owner.Cursor;
        _applied = cursor;
        owner.SetCurrentValue(InputElement.CursorProperty, cursor);
    }

    /// <summary>
    /// Возвращает курсор, который стоял до жеста.
    /// </summary>
    /// <remarks>
    /// Повторный вызов ничего не делает: выход из состояния проходит и на отпускании,
    /// и на потере захвата, а восстановить прежний курсор нужно ровно один раз.
    /// </remarks>
    public void Restore()
    {
        if (_owner == null)
            return;

        _owner.SetCurrentValue(InputElement.CursorProperty, _previous);
        _owner = null;
        _previous = null;
        _applied = null;
    }
}
