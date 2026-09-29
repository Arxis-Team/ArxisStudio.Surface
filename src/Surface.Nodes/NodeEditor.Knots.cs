using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

// Узлы перенаправления: куда смотрят касательные их связей (ADR 0013). Часть NodeEditor; общее
// описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    // Узлы перенаправления по ключам обоих их портов. Держатся и свёрнутые: их связи без контрола —
    // в упрощённом виде и за краем окна — рисуются той же кривой, что и с контролом.
    private readonly Dictionary<object, Knot> _knotByKey = new();

    /// <summary>
    /// Развёрнут ли узел перенаправления, которому принадлежит порт с этим ключом, — для тестов.
    /// </summary>
    internal bool IsKnotReversed(object key) => _knotByKey.TryGetValue(key, out var knot) && knot.Reversed;

    /// <summary>
    /// Сколько ключей портов узлов перенаправления помнит редактор, — для тестов: ушедший узел забыт.
    /// </summary>
    internal int KnotKeys => _knotByKey.Count;

    /// <summary>
    /// Запоминает порты узла перенаправления и сразу решает, куда он смотрит.
    /// </summary>
    /// <remarks>
    /// Зовёт <see cref="Reroute"/>, попав в редактор и сменив ключи. Прежние ключи забываются сами, когда
    /// у них не остаётся ни связи, ни живого порта.
    /// </remarks>
    internal void AttachKnot(object? input, object? output)
    {
        if (input == null || output == null || Equals(input, output))
            return;

        if (_knotByKey.TryGetValue(input, out var known) && Equals(known.Output, output))
            return;

        Unmap(input);
        Unmap(output);

        var knot = new Knot(input, output);
        _knotByKey[input] = knot;
        _knotByKey[output] = knot;
        UpdateKnot(knot);
    }

    /// <summary>
    /// Развёрнут ли конец связи: он у порта развёрнутого узла перенаправления.
    /// </summary>
    private bool IsReversedEnd(object? key, LinkEnd end) =>
        key != null
        && _knotByKey.TryGetValue(key, out var knot)
        && knot.Reversed
        && Equals(end == LinkEnd.Source ? knot.Output : knot.Input, key);

    /// <summary>
    /// Пересматривает узел перенаправления на конце связи: сдвинулся её другой конец или она ушла.
    /// </summary>
    private void UpdateKnotAt(object? key)
    {
        if (key != null && _knotByKey.TryGetValue(key, out var knot))
            UpdateKnot(knot);
    }

    /// <summary>
    /// Забывает узел перенаправления, у ключа которого не осталось ни связи, ни живого порта: узел ушёл
    /// из графа, и держать его данные незачем.
    /// </summary>
    private void SettleKnotAt(object? key)
    {
        if (key == null || !_knotByKey.TryGetValue(key, out var knot))
            return;

        if (_linksByKey.ContainsKey(knot.Input) || _linksByKey.ContainsKey(knot.Output)
            || Ports.Find(knot.Input) != null || Ports.Find(knot.Output) != null)
        {
            UpdateKnot(knot);
            return;
        }

        Unmap(knot.Input);
    }

    private void Unmap(object key)
    {
        if (!_knotByKey.Remove(key, out var knot))
            return;

        var other = Equals(knot.Input, key) ? knot.Output : knot.Input;
        if (_knotByKey.TryGetValue(other, out var paired) && ReferenceEquals(paired, knot))
            _knotByKey.Remove(other);
    }

    /// <summary>
    /// Разворачивает узел перенаправления, если провод идёт через него справа налево, и пересчитывает
    /// его связи, если решение сменилось.
    /// </summary>
    /// <remarks>
    /// Правило — как у узла перенаправления в Blueprint: сравниваются средние по горизонтали дальних
    /// концов входящих и исходящих связей. Есть связи только с одной стороны — сравнивается с центром
    /// самого узла. Ответ меняют только концы связей, а не касательные, поэтому пересчёт связей здесь же
    /// решение не качает.
    /// </remarks>
    private void UpdateKnot(Knot knot)
    {
        var reversed = Reversed(knot);
        if (reversed == knot.Reversed)
            return;

        knot.Reversed = reversed;
        RefreshLinksAt(knot.Input);
        RefreshLinksAt(knot.Output);
    }

    private bool Reversed(Knot knot)
    {
        double left = 0, right = 0;
        int lefts = 0, rights = 0;
        Point? centre = null;

        if (_linksByKey.TryGetValue(knot.Input, out var incoming))
        {
            foreach (var record in incoming)
            {
                if (!record.IsResolved || !Equals(record.Target, knot.Input))
                    continue;

                left += record.Geometry.Source.X;
                lefts++;
                centre ??= record.Geometry.Target;
            }
        }

        if (_linksByKey.TryGetValue(knot.Output, out var outgoing))
        {
            foreach (var record in outgoing)
            {
                if (!record.IsResolved || !Equals(record.Source, knot.Output))
                    continue;

                right += record.Geometry.Target.X;
                rights++;
                centre ??= record.Geometry.Source;
            }
        }

        if (lefts > 0 && rights > 0)
            return right / rights < left / lefts;

        if (centre is not { } c)
            return false;

        if (lefts > 0)
            return c.X < left / lefts;

        return rights > 0 && right / rights < c.X;
    }

    /// <summary>
    /// Порты узла перенаправления и куда он смотрит.
    /// </summary>
    private sealed class Knot(object input, object output)
    {
        public object Input { get; } = input;

        public object Output { get; } = output;

        /// <summary>
        /// Провод идёт через узел справа налево: во вход он приходит справа, из выхода уходит влево.
        /// </summary>
        public bool Reversed { get; set; }
    }
}
