using HomeStack.Modules.Inventory;

namespace HomeStack.Tests;

public class CategoryTests
{
    [Theory]
    [InlineData(new[] { "en:dairies", "en:cheeses" }, "cheese")]
    [InlineData(new[] { "en:meats", "en:poultry" }, "poultry")]
    [InlineData(new[] { "en:plant-based-foods", "en:pastas" }, "dry-goods")]
    [InlineData(new[] { "en:frozen-foods", "en:vegetables" }, "frozen")]
    [InlineData(new string[0], "other")]
    public void Maps_open_food_facts_tags(string[] tags, string expected) =>
        Assert.Equal(expected, OpenFoodFactsClient.MapCategory(tags));

    [Fact]
    public void Product_overrides_beat_category_defaults()
    {
        var p = new Product { Name = "Homemade stock", Category = "leftovers", FreezerDays = 365 };
        Assert.Equal(365, Categories.ShelfLifeDays(p, LocationKind.Freezer));
        Assert.Equal(4, Categories.ShelfLifeDays(p, LocationKind.Fridge));
        Assert.False(Categories.IsFreezable(new Product { Name = "Eggs", Category = "eggs" }));
    }
}
