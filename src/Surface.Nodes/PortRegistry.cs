using System.Collections.Generic;
using Avalonia.Logging;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Живые порты редактора по их ключу — данным порта.
/// </summary>
/// <remarks>
/// Связь знает свои концы по данным портов (ADR 0004), и найти по ним живой порт — работа этого
/// реестра. Ключи сравниваются обычным равенством: приложение вправе ключевать порты числами или
/// строками, а не только ссылками на свои объекты.
/// <para>
/// На одном ключе может оказаться несколько портов, и держатся они все, а отвечает первый. Так
/// бывает законно — пересозданный контейнер регистрирует новый порт раньше, чем уходит старый, — и
/// по ошибке: порт, поставленный руками без <see cref="Port.Data"/>, ключуется данными своего узла.
/// Второй случай пишется в журнал, а не бросает: редактор не должен падать из-за разметки хоста.
/// </para>
/// </remarks>
internal sealed class PortRegistry
{
    private readonly Dictionary<object, List<Port>> _ports = new();

    /// <summary>
    /// Возникает, когда по ключу появился порт или ушёл последний.
    /// </summary>
    public event System.Action<object>? Changed;

    public void Register(object key, Port port)
    {
        if (!_ports.TryGetValue(key, out var list))
        {
            list = new List<Port>(1);
            _ports.Add(key, list);
        }

        if (list.Contains(port))
            return;

        list.Add(port);

        if (list.Count > 1)
        {
            Logger.TryGet(LogEventLevel.Warning, "Nodes")?.Log(
                port,
                "Порт с ключом {Key} уже есть на поверхности; отвечает первый. Порт, поставленный без Data, ключуется данными своего узла.",
                key);
        }
        else
        {
            Changed?.Invoke(key);
        }
    }

    /// <summary>
    /// Снимает ровно этот экземпляр порта, а не всё, что лежит на ключе.
    /// </summary>
    /// <remarks>
    /// Иначе старый порт, уходящий после регистрации своего преемника, снимал бы и преемника.
    /// </remarks>
    public void Unregister(object key, Port port)
    {
        if (!_ports.TryGetValue(key, out var list) || !list.Remove(port))
            return;

        if (list.Count == 0)
        {
            _ports.Remove(key);
            Changed?.Invoke(key);
        }
        else
        {
            // Отвечает теперь другой экземпляр — связям, если они есть, надо пересчитать концы.
            Changed?.Invoke(key);
        }
    }

    public Port? Find(object? key) =>
        key != null && _ports.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;

    public int Count => _ports.Count;

    /// <summary>
    /// Отвечающие порты по ключам — на жест, которому нужен снимок.
    /// </summary>
    public IEnumerable<(object Key, Port Port)> Snapshot()
    {
        foreach (var pair in _ports)
        {
            if (pair.Value.Count > 0)
                yield return (pair.Key, pair.Value[0]);
        }
    }
}
