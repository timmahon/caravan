using Caravan.Model;

namespace Caravan.Management;

/// <summary>The counters of one node of a route, with what management shows about it.</summary>
internal sealed class ProcessorCounters(ProcessorDefinition definition, string nodeId, int order)
{
    public ProcessorDefinition Definition { get; } = definition;

    public string NodeId { get; } = nodeId;

    public int Order { get; } = order;

    public ProcessingCounters Counters { get; } = new();

    public int Level
    {
        get
        {
            var level = 1;
            for (var parent = Definition.Parent; parent is not null; parent = parent.Parent)
            {
                level++;
            }

            return level;
        }
    }
}
