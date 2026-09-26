using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Значительная часть interaction-логики редактора (политики перемещения и resize,
// групповые операции, резолв selection target) намеренно internal.
// Тестам она нужна напрямую, поэтому сборка тестов видит внутренности.
[assembly: InternalsVisibleTo("ArxisStudio.DesignEditor.Tests")]

// Один адрес разметки на все три слоя (ADR 0003). Он переживёт переезд типов
// в отдельные сборки, а clr-namespace — нет; поэтому короткие имена во всех
// трёх пространствах обязаны быть уникальны, и это проверяет LayerDependencyTests.
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface")]
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.Editing")]
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.UiDesigner")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
