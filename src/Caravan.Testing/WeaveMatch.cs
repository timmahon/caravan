using Caravan.Model;

namespace Caravan.Testing;

/// <summary>A node of a route's definition tree found while weaving, with its parent (null at the top level) and depth (1 at the top level).</summary>
internal sealed record WeaveMatch(ProcessorDefinition Node, ProcessorDefinition? Parent, int Depth);
