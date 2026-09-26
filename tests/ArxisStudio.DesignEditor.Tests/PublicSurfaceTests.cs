using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Закрепляет публичную поверхность библиотеки — до отдельного члена.
/// </summary>
/// <remarks>
/// Тест не защищает конкретный слепок — он делает расширение поверхности осознанным.
/// Новый публичный член роняет тест, и автор обязан либо внести его в baseline,
/// приняв на себя обязательство поддерживать, либо сделать член internal.
/// <para>
/// Прежняя версия сверяла только <c>GetExportedTypes()</c>, то есть список типов.
/// Этого хватало ровно до первого нового публичного <b>члена</b>: <c>SelectDesignTarget</c>
/// приехал с девятью дефектами, не уронив ни одного теста, потому что список типов
/// не изменился. Слепок теперь идёт до члена и включает значения по умолчанию у
/// <c>AvaloniaProperty</c> — смена умолчания ломающая и не видна ни в одной сигнатуре.
/// </para>
/// <para>
/// Обновление baseline: <c>UPDATE_PUBLIC_SURFACE=1 dotnet test --filter Public_Surface</c>.
/// Правка baseline руками тоже допустима — это обычный текстовый файл, и его диff
/// в ревью и есть тот артефакт, ради которого тест существует.
/// </para>
/// </remarks>
public class PublicSurfaceTests
{
    private const string BaselineResource = "ArxisStudio.Tests.PublicSurface.baseline.txt";
    private const string UpdateVariable = "UPDATE_PUBLIC_SURFACE";

    [Fact]
    public void Public_Surface_Matches_The_Baseline()
    {
        var actual = Normalize(PublicSurface.Build());

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(BaselineSourcePath(), actual);
            return;
        }

        var expected = Normalize(ReadBaseline());

        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;

        Assert.Fail(
            "Публичная поверхность разошлась с baseline.\n" +
            $"Обновить: {UpdateVariable}=1 dotnet test --filter Public_Surface\n\n" +
            Describe(expected, actual));
    }

    /// <summary>
    /// Показывает расхождение построчно: у слепка в несколько сотен строк
    /// целиковое сравнение читать невозможно.
    /// </summary>
    private static string Describe(string expected, string actual)
    {
        var before = expected.Split('\n');
        var after = actual.Split('\n');

        var added = after.Except(before, StringComparer.Ordinal).ToList();
        var removed = before.Except(after, StringComparer.Ordinal).ToList();

        var report = new List<string>();

        if (added.Count > 0)
            report.Add("Появилось (внести в baseline или сделать internal):\n  " + string.Join("\n  ", added));

        if (removed.Count > 0)
            report.Add("Исчезло (это ломающее изменение):\n  " + string.Join("\n  ", removed));

        return string.Join("\n\n", report);
    }

    private static string ReadBaseline()
    {
        using var stream = typeof(PublicSurfaceTests).Assembly.GetManifestResourceStream(BaselineResource)
                           ?? throw new InvalidOperationException(
                               $"Ресурс {BaselineResource} не найден. Ожидается EmbeddedResource в тестовом проекте.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Путь к baseline в исходниках — только для режима обновления.
    /// </summary>
    /// <remarks>
    /// <see cref="CallerFilePathAttribute"/> даёт путь на момент компиляции; на CI
    /// компиляция и прогон идут на одной машине, а режим обновления там и не нужен.
    /// Чтение при сравнении идёт из встроенного ресурса и от путей не зависит.
    /// </remarks>
    private static string BaselineSourcePath([CallerFilePath] string? thisFile = null) =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "PublicSurface.baseline.txt");

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    [Fact]
    public void State_Machines_Are_Not_Public()
    {
        // Сегмент, а не префикс: при разделении на слои машины состояний уезжают
        // в ArxisStudio.Surface.States, и страж обязан следовать за ними.
        var leaked = Leaked(name => HasSegment(name, "States"));

        Assert.True(leaked.Count == 0, "Машины состояний должны быть internal:\n" + string.Join("\n", leaked));
    }

    [Fact]
    public void Placement_Strategies_Are_Not_Public()
    {
        // Форму стратегий ещё рано фиксировать: она должна отлежаться внутри
        // библиотеки. Добавить публичный тип позже — аддитивно, опубликовать
        // неверный сейчас — ломающе.
        var leaked = Leaked(name => HasSegment(name, "Placement"));

        Assert.True(leaked.Count == 0, "Стратегии размещения должны быть internal:\n" + string.Join("\n", leaked));
    }

    [Fact]
    public void Guide_Arithmetic_Is_Not_Public()
    {
        // Слой и модель направляющих опубликованы сознательно: без них слой нечем
        // заменить снаружи. Резолверы — нет: это арифметика, у которой нет потребителя
        // за пределами библиотеки, а форма у неё меняется с каждым новым правилом.
        var leaked = Leaked(name => name.Contains("Resolver", StringComparison.Ordinal));

        Assert.True(leaked.Count == 0, "Резолверы должны быть internal:\n" + string.Join("\n", leaked));
    }

    [Fact]
    public void Overlay_Implementation_Is_Not_Public()
    {
        var leaked = Leaked(name => name.Contains("SelectionAdornerLayer", StringComparison.Ordinal)
                                    || name.Contains("SelectionAdornerInfo", StringComparison.Ordinal)
                                    || name.Contains("DesignSurface", StringComparison.Ordinal));

        Assert.True(leaked.Count == 0, "Детали overlay должны быть internal:\n" + string.Join("\n", leaked));
    }

    private static List<string> Leaked(Func<string, bool> predicate) =>
        PublicSurface.ExportedTypes().Select(type => type.FullName!).Where(predicate).ToList();

    /// <summary>
    /// Лежит ли тип в пространстве имён, одним из сегментов которого является <paramref name="segment"/>.
    /// </summary>
    private static bool HasSegment(string fullName, string segment)
    {
        var lastDot = fullName.LastIndexOf('.');
        var ns = lastDot < 0 ? string.Empty : fullName[..lastDot];
        return ns.Split('.').Contains(segment, StringComparer.Ordinal);
    }
}
