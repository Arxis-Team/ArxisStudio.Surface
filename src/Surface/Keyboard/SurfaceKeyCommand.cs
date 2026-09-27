using System;
using Avalonia.Input;

namespace ArxisStudio.Surface;

/// <summary>
/// Клавиатурная команда поверхности: какое нажатие она слушает и что делает.
/// </summary>
/// <remarks>
/// Узнавание и исполнение разделены намеренно. Сочетание часто нельзя назвать заранее:
/// отмена берётся у платформы или у <see cref="SurfaceInputGestures"/>, и узнать её
/// можно только в момент нажатия. А исполнение вправе отказаться — стрелка без
/// выделения не должна пропадать, — и тогда нажатие достаётся следующей команде.
/// </remarks>
public sealed class SurfaceKeyCommand
{
    private readonly Func<SurfaceView, KeyEventArgs, bool> _matches;
    private readonly Func<SurfaceView, KeyEventArgs, bool> _execute;

    /// <summary>
    /// Создаёт команду с произвольным правилом узнавания.
    /// </summary>
    /// <param name="id">Идентификатор; по нему команду заменяют и снимают.</param>
    /// <param name="matches">Узнаёт ли команда нажатие.</param>
    /// <param name="execute">
    /// Исполняет команду и отвечает, выполнена ли она. <see langword="false"/> оставляет
    /// нажатие необработанным: его получит следующая команда и затем приложение.
    /// </param>
    public SurfaceKeyCommand(
        string id,
        Func<SurfaceView, KeyEventArgs, bool> matches,
        Func<SurfaceView, KeyEventArgs, bool> execute)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Идентификатор команды не может быть пустым.", nameof(id));

        Id = id;
        _matches = matches ?? throw new ArgumentNullException(nameof(matches));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    /// <summary>
    /// Создаёт команду на одно сочетание клавиш.
    /// </summary>
    /// <param name="id">Идентификатор; по нему команду заменяют и снимают.</param>
    /// <param name="gesture">Сочетание, которое слушает команда.</param>
    /// <param name="execute">Исполняет команду и отвечает, выполнена ли она.</param>
    public SurfaceKeyCommand(string id, KeyGesture gesture, Func<SurfaceView, bool> execute)
        : this(
            id,
            MatchGesture(gesture ?? throw new ArgumentNullException(nameof(gesture))),
            Wrap(execute ?? throw new ArgumentNullException(nameof(execute))))
    {
    }

    /// <summary>
    /// Идентификатор команды.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Узнаёт ли команда нажатие.
    /// </summary>
    /// <param name="view">Поверхность, получившая нажатие.</param>
    /// <param name="e">Аргументы нажатия.</param>
    public bool Matches(SurfaceView view, KeyEventArgs e) => _matches(view, e);

    /// <summary>
    /// Исполняет команду.
    /// </summary>
    /// <param name="view">Поверхность, получившая нажатие.</param>
    /// <param name="e">Аргументы нажатия.</param>
    /// <returns><see langword="true"/>, если команда выполнена и нажатие обработано.</returns>
    public bool Execute(SurfaceView view, KeyEventArgs e) => _execute(view, e);

    private static Func<SurfaceView, KeyEventArgs, bool> MatchGesture(KeyGesture gesture) =>
        (_, e) => gesture.Matches(e);

    private static Func<SurfaceView, KeyEventArgs, bool> Wrap(Func<SurfaceView, bool> execute) =>
        (view, _) => execute(view);
}
