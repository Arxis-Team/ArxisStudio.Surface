using System;

namespace ArxisStudio.Surface;

/// <summary>
/// Запрос на отмену или повтор правки.
/// </summary>
/// <remarks>
/// Стека правок у редактора нет и быть не может: он сообщает о своих изменениях через
/// <see cref="SurfaceView.EditCompleted"/> и умеет применить одно из них
/// (<see cref="SurfaceView.Revert"/> / <see cref="SurfaceView.Reapply"/>), но что
/// именно отменять и в каком порядке — знает только тот, кто эти изменения копил.
/// <para>
/// Поэтому клавиши истории поднимают запрос, как удаление и перестановка: пока
/// обработчик не выставил <see cref="Handled"/>, нажатие остаётся необработанным
/// и всплывает дальше.
/// </para>
/// </remarks>
public sealed class DesignEditorHistoryRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Получает или задает признак того, что запрос выполнен.
    /// </summary>
    public bool Handled { get; set; }
}
