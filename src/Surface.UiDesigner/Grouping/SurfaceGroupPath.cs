using System;
using System.Collections.Generic;
using System.Text;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Арифметика пути design-time группы.
/// </summary>
/// <remarks>
/// Пометка группы — путь от внешней группы к внутренней: <c>"group-2/group-1"</c> означает
/// «участник <c>group-1</c>, которая лежит в <c>group-2</c>». Плоская пометка остаётся
/// валидной: это путь из одного сегмента.
/// <para>
/// Вынесено отдельно, потому что путь разбирают трое: сама группировка, оверлей
/// (кластеры выделения) и жесты. Держать разбор в одном из них значило бы, что двое
/// других повторят его по-своему.
/// </para>
/// </remarks>
internal static class SurfaceGroupPath
{
    /// <summary>
    /// Разделитель сегментов.
    /// </summary>
    /// <remarks>
    /// Часть контракта пометки: в сегменте его быть не может, иначе один идентификатор
    /// читался бы как два уровня. Проверяют это точки записи, а не <c>SurfaceGroup.SetId</c>:
    /// сырое присваивание остаётся сырым, как и раньше.
    /// </remarks>
    public const char Separator = '/';

    /// <summary>Разбирает путь на сегменты.</summary>
    public static string[] Split(string? path) =>
        string.IsNullOrEmpty(path)
            ? Array.Empty<string>()
            : path!.Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Собирает путь из сегментов; пустые пропускаются.</summary>
    public static string? Combine(IEnumerable<string?> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
                continue;

            if (builder.Length > 0)
                builder.Append(Separator);

            builder.Append(segment);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Приписывает сегмент к пути.</summary>
    public static string? Append(string? path, string? segment) => Combine(new[] { path, segment });

    /// <summary>Возвращает путь родительской группы или <see langword="null"/> для верхнего уровня.</summary>
    public static string? Parent(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var index = path!.LastIndexOf(Separator);
        return index <= 0 ? null : path.Substring(0, index);
    }

    /// <summary>Возвращает последний сегмент пути.</summary>
    public static string? Leaf(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var index = path!.LastIndexOf(Separator);
        return index < 0 ? path : path.Substring(index + 1);
    }

    /// <summary>Число уровней в пути.</summary>
    public static int Depth(string? path) => Split(path).Length;

    /// <summary>
    /// Признак того, что путь лежит внутри предка (или совпадает с ним).
    /// </summary>
    /// <remarks>
    /// Пустой предок — это корень формы, и внутри него лежит любая пометка.
    /// Контрол без пометки не лежит нигде, поэтому <see langword="null"/> у пути
    /// даёт <see langword="false"/> при любом предке.
    /// </remarks>
    public static bool IsInside(string? path, string? ancestor)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (string.IsNullOrEmpty(ancestor))
            return true;

        return path!.Length > ancestor!.Length
            ? path.StartsWith(ancestor, StringComparison.Ordinal) && path[ancestor.Length] == Separator
            : string.Equals(path, ancestor, StringComparison.Ordinal);
    }

    /// <summary>
    /// Возвращает участок пути на один уровень глубже открытой группы.
    /// </summary>
    /// <remarks>
    /// Это и есть кластер, который пользователь видит одной рамкой: внутри открытой
    /// группы видны её прямые дети, снаружи — самые внешние группы. Возвращает
    /// <see langword="null"/>, если контрол не лежит внутри открытой группы или лежит
    /// ровно на её уровне — тогда рамкой становится он сам.
    /// </remarks>
    public static string? ClusterOf(string? path, string? entered)
    {
        if (!IsInside(path, entered) || string.Equals(path, entered, StringComparison.Ordinal))
            return null;

        var depth = Depth(entered) + 1;
        var segments = Split(path);
        if (segments.Length < depth)
            return null;

        var head = new string[depth];
        Array.Copy(segments, head, depth);
        return Combine(head);
    }

    /// <summary>Возвращает наибольший общий префикс двух путей.</summary>
    public static string? CommonPrefix(string? left, string? right)
    {
        var a = Split(left);
        var b = Split(right);
        var shared = new List<string>();

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                break;

            shared.Add(a[i]);
        }

        return Combine(shared);
    }

    /// <summary>
    /// Заменяет префикс пути другим.
    /// </summary>
    /// <remarks>
    /// Так переезжает целое поддерево: переименование уровня и вставка нового
    /// внешнего — это одна и та же операция над префиксом.
    /// </remarks>
    public static string? Rebase(string? path, string? oldPrefix, string? newPrefix)
    {
        if (!IsInside(path, oldPrefix))
            return path;

        var tail = Split(path);
        var skip = Depth(oldPrefix);
        var rest = new List<string?> { newPrefix };

        for (var i = skip; i < tail.Length; i++)
            rest.Add(tail[i]);

        return Combine(rest);
    }

    /// <summary>Признак того, что сегмент годится как идентификатор уровня.</summary>
    public static bool IsValidSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment) && segment!.IndexOf(Separator) < 0;
}
