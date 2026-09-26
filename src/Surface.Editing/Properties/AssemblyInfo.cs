using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Службы инструментов internal по той же причине, что и швы ядра (ADR 0003):
// дизайнер форм и редактор узлов подключают их как дружественные сборки.
[assembly: InternalsVisibleTo("ArxisStudio.Surface.UiDesigner")]
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Nodes")]
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Tests")]

[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.Editing")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
