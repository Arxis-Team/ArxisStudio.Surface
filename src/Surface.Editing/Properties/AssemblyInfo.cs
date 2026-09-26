using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Службы инструментов internal по той же причине, что и швы ядра (ADR 0003):
// дизайнер форм подключает их как дружественная сборка.
[assembly: InternalsVisibleTo("ArxisStudio.Surface.UiDesigner")]
[assembly: InternalsVisibleTo("ArxisStudio.DesignEditor.Tests")]

[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.Editing")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
