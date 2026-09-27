namespace ArxisStudio.Surface.Nodes.States;

/// <summary>
/// Отцепляемый конец существующей связи: сама связь, какой её конец и к какому порту он был
/// прицеплен.
/// </summary>
internal sealed record LinkDetachment(LinkRecord Link, LinkEnd End, object Port);
