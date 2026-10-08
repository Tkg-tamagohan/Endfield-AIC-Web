using EndfieldAicWeb.Application.Graph;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>FG: 生産フローグラフのモデル構築（test-specification-phase15.md §2）。</summary>
public partial class FlowGraphModelBuilderTests
{
    private static (ProductionPlan Plan, FlowGraphModel Model) Build(
        MasterDataSnapshot snapshot,
        bool unadjusted = false,
        bool expandFacilities = false,
        ContextFilter? context = null,
        IReadOnlyCollection<string>? specifiedBaseItemIds = null,
        params ProductionTarget[] targets)
    {
        ContextFilter ctx = context ?? new ContextFilter();
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot, targets, ctx, [], [], []);
        return (plan, FlowGraphModelBuilder.Build(
            plan, snapshot, ctx, targets, unadjusted, expandFacilities, specifiedBaseItemIds));
    }

    private static FlowGraphNode Node(FlowGraphModel model, string id) =>
        Assert.Single(model.Nodes, n => n.Id == id);

    private static FlowGraphEdge Edge(
        FlowGraphModel model, string fromId, string toId, FlowGraphEdgeKind kind) =>
        Assert.Single(model.Edges, e => e.FromId == fromId && e.ToId == toId && e.Kind == kind);
}
