using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Швы ядра (геометрия, резолвер target'ов, политики, службы, хуки) намеренно internal:
// их форма должна отлежаться до второго потребителя (ADR 0003). Слои выше живут в своих
// сборках и ходят в эти швы как дружественные; тестам внутренности нужны напрямую.
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Editing")]
[assembly: InternalsVisibleTo("ArxisStudio.Surface.UiDesigner")]
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Nodes")]
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Tests")]

// Один адрес разметки на все слои: каждая сборка объявляет под ним своё
// пространство. Короткие имена во всех обязаны быть уникальны — это
// проверяет LayerDependencyTests.
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
