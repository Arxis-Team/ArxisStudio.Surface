namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Идентификаторы клавиатурных команд редактора узлов в <see cref="SurfaceView.KeyCommands"/>.
/// </summary>
/// <remarks>
/// Команды редактора узлов стоят в наборе впереди встроенных: они отвечают только тогда, когда им
/// есть что делать, и иначе уступают клавишу — так же, как любая команда набора.
/// </remarks>
public static class NodeEditorKeyCommands
{
    /// <summary>Отмена протягиваемой связи по Escape.</summary>
    public const string CancelLink = "nodes.cancelLink";
}
