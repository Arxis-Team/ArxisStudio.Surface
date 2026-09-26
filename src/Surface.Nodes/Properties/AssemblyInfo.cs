using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Внутренности редактора узлов открыты тестам — как у трёх других слоёв.
[assembly: InternalsVisibleTo("ArxisStudio.Surface.Tests")]

[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio.Surface", "ArxisStudio.Surface.Nodes")]
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio.Surface", "surface")]
