using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Тестам внутренности дизайнера интерфейса нужны напрямую.
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Tests")]

[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.UiDesigner")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
