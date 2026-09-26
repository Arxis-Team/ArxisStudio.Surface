using System;
using Avalonia;

namespace ArxisStudio.Surface;

/// <summary>
/// Переносит значение свойства одного контрола в свойство другого.
/// </summary>
/// <remarks>
/// Нужен там, где хост ставит контрол-спутник рядом с редактором и задаёт только
/// <c>Editor</c>: линейке и слою направляющих иначе пришлось бы требовать по пять-шесть
/// привязок, каждая из которых обязана совпадать с остальными.
/// <para>
/// Значение ставится через <c>SetCurrentValue</c>, поэтому собственная привязка хоста
/// к тому же свойству переживает перенос и не затирается насовсем.
/// </para>
/// </remarks>
internal static class PropertyMirror
{
    /// <summary>
    /// Подписывается на свойство источника и переносит его в свойство приёмника.
    /// </summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="source">Источник значения.</param>
    /// <param name="sourceProperty">Свойство источника.</param>
    /// <param name="target">Приёмник значения.</param>
    /// <param name="targetProperty">Свойство приёмника.</param>
    /// <returns>Подписка, снимающая перенос.</returns>
    public static IDisposable Bind<T>(
        AvaloniaObject source,
        AvaloniaProperty<T> sourceProperty,
        AvaloniaObject target,
        AvaloniaProperty<T> targetProperty)
        => source.GetObservable(sourceProperty).Subscribe(new Sink<T>(target, targetProperty));

    /// <summary>
    /// Объединяет несколько подписок в одну.
    /// </summary>
    /// <param name="subscriptions">Подписки.</param>
    public static IDisposable Combine(params IDisposable[] subscriptions) => new Composite(subscriptions);

    private sealed class Sink<T> : IObserver<T>
    {
        private readonly AvaloniaObject _target;
        private readonly AvaloniaProperty<T> _property;

        public Sink(AvaloniaObject target, AvaloniaProperty<T> property)
        {
            _target = target;
            _property = property;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => _target.SetCurrentValue(_property, value);
    }

    private sealed class Composite : IDisposable
    {
        private readonly IDisposable[] _subscriptions;

        public Composite(IDisposable[] subscriptions) => _subscriptions = subscriptions;

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}
