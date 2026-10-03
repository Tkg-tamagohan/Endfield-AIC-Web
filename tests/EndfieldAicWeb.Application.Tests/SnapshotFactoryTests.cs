using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using DomainEnvironment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>SNP: マスタスナップショット構築。</summary>
public class SnapshotFactoryTests
{
    // SNP-01: MasterDocument の 5 エンティティがスナップショットへ全件引き継がれる。
    [Fact]
    public void SnapshotCarriesAllEntityCollections()
    {
        var document = new MasterDocument
        {
            SchemaVersion = 1,
            DataVersion = "test",
            Items = [new Item { Id = "i-1", Name = "素材", Category = "", VersionAdded = "1.0.0" }],
            Facilities = [new Facility { Id = "f-1", Name = "設備", PowerConsumption = 10, VersionAdded = "1.0.0" }],
            Environments = [new DomainEnvironment { Id = "env-1", Name = "環境", ProviderFacilityId = "f-1", ConsumeItemId = "i-1", ConsumeRatePerMinute = 60, CoverableMachines = 4, VersionAdded = "1.0.0" }],
            GameEvents = [new GameEvent { Id = "ev-1", Name = "イベント", VersionAdded = "1.0.0" }],
            Recipes = [new Recipe { Id = "r-1", Name = "レシピ", VersionAdded = "1.0.0" }],
        };

        MasterDataSnapshot snapshot = MasterSnapshotFactory.Create(document);

        Assert.Single(snapshot.Items);
        Assert.Single(snapshot.Facilities);
        Assert.Single(snapshot.Environments);
        Assert.Single(snapshot.GameEvents);
        Assert.Single(snapshot.Recipes);
    }

    // SNP-02: 構築したスナップショットで計算が成立する（Id 索引が解決される）。
    [Fact]
    public void SnapshotIsUsableForCalculation()
    {
        var document = new MasterDocument
        {
            SchemaVersion = 1,
            DataVersion = "test",
            Items = [
                new Item { Id = "i-u", Name = "上流素材", Category = "", IsGatherable = true, VersionAdded = "1.0.0" },
                new Item { Id = "i-p", Name = "加工品", Category = "", VersionAdded = "1.0.0" },
            ],
            Facilities = [new Facility { Id = "f-1", Name = "加工機", PowerConsumption = 10, VersionAdded = "1.0.0" }],
            Environments = [],
            GameEvents = [],
            Recipes = [
                new Recipe
                {
                    Id = "r-p",
                    Name = "加工品",
                    Inputs = [new RecipeInput { ItemId = "i-u", Quantity = 1 }],
                    Outputs = [new RecipeOutput { ItemId = "i-p", Quantity = 1 }],
                    Facilities = [new RecipeFacility { RecipeId = "r-p", FacilityId = "f-1", CycleTime = 2 }],
                    VersionAdded = "1.0.0",
                },
            ],
        };

        MasterDataSnapshot snapshot = MasterSnapshotFactory.Create(document);
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot,
            [new ProductionTarget("i-p", 30)],
            new ContextFilter(),
            [],
            [],
            []);

        ItemRequirement requirement = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-p");
        Assert.Equal(30, requirement.RequiredPerMinute);
        Assert.Equal(0, requirement.UnmetPerMinute);
    }
}
