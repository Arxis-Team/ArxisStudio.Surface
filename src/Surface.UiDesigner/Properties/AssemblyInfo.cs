using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Тестам внутренности дизайнера форм нужны напрямую.
[assembly: InternalsVisibleTo("ArxisStudio.DesignEditor.Tests")]

[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.UiDesigner")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
