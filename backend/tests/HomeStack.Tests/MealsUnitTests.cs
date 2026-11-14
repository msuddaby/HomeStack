using HomeStack.Modules.Inventory;
using HomeStack.Modules.Meals;

namespace HomeStack.Tests;

public class IngredientParserTests
{
    [Theory]
    [InlineData("2 cups flour", 2.0, "cup", "flour")]
    [InlineData("1 1/2 tsp salt", 1.5, "tsp", "salt")]
    [InlineData("1½ cups sugar", 1.5, "cup", "sugar")]
    [InlineData("½ cup milk", 0.5, "cup", "milk")]
    [InlineData("3 large eggs", 3.0, null, "large eggs")]
    [InlineData("200g chicken breast", 200.0, "g", "chicken breast")]
    [InlineData("1 (14 oz) can diced tomatoes", 1.0, "can", "diced tomatoes")]
    [InlineData("1 onion, finely chopped", 1.0, null, "onion")]
    [InlineData("2-3 cloves garlic", 2.0, "cloves", "garlic")]
    [InlineData("a pinch of nutmeg", null, "pinch", "nutmeg")]
    [InlineData("Salt and pepper to taste", null, null, "Salt and pepper")]
    [InlineData("1 lb. ground beef", 1.0, "lb", "ground beef")]
    public void Parses_common_lines(string line, double? qty, string? unit, string name)
    {
        var p = IngredientParser.Parse(line);
        Assert.Equal(qty, p.Quantity);
        Assert.Equal(unit, p.Unit);
        Assert.Equal(name, p.Name);
    }

    [Fact]
    public void Captures_notes_and_optional()
    {
        var p = IngredientParser.Parse("1 (14 oz) can tomatoes, drained (optional)");
        Assert.True(p.Optional);
        Assert.Contains("14 oz", p.Note);
        Assert.Contains("drained", p.Note);
    }
}

public class UnitsTests
{
    [Theory]
    [InlineData(200, "g", "kg", 0.2)]
    [InlineData(1, "lb", "g", 453.592)]
    [InlineData(2, "tbsp", "tsp", 6.0)]
    [InlineData(3, null, "pc", 3)]
    [InlineData(1, "dozen", null, 12)]
    [InlineData(2, "can", "can", 2)]
    public void Converts_within_family(double amount, string? from, string? to, double expected) =>
        Assert.Equal(expected, Units.Convert(amount, from, to)!.Value, 2);

    [Fact]
    public void Refuses_cross_family() => Assert.Null(Units.Convert(1, "cup", "g"));
}

public class ProductMatcherTests
{
    private static ProductDto P(int id, string name) => new(id, name, null, "other", null, null, null, null, null, null, null);
    private static readonly List<ProductDto> Products =
        [P(1, "Eggs"), P(2, "2% Milk"), P(3, "Chicken thighs"), P(4, "Peanut butter"), P(5, "Butter"), P(6, "Tomatoes"), P(7, "Sour cream")];

    [Theory]
    [InlineData("large eggs", 1)]
    [InlineData("egg", 1)]
    [InlineData("milk", 2)]
    [InlineData("boneless skinless chicken thighs", 3)]
    [InlineData("butter", 5)]
    [InlineData("unsalted butter", 5)]
    [InlineData("diced tomatoes", 6)]
    public void Matches(string ingredient, int expectedId) => Assert.Equal(expectedId, ProductMatcher.Match(ingredient, Products)?.Id);

    [Theory]
    [InlineData("cream")]
    [InlineData("flour")]
    public void Does_not_overmatch(string ingredient) => Assert.Null(ProductMatcher.Match(ingredient, Products));

    [Fact]
    public void Butter_alone_does_not_match_peanut_butter() =>
        Assert.Null(ProductMatcher.Match("butter", [P(4, "Peanut butter")]));
}

public class RecipeImporterTests
{
    private const string Html = """
        <html><head>
        <script type="application/ld+json">{"@context":"https://schema.org","@type":"WebSite","name":"Food Blog"}</script>
        <script type="application/ld+json">
        {"@context":"https://schema.org","@graph":[
          {"@type":"WebPage","name":"x"},
          {"@type":["Recipe","NewsArticle"],"name":"Weeknight Chili &amp; Rice","description":"<p>Cosy.</p>",
           "image":[{"@type":"ImageObject","url":"https://example.com/chili.jpg"}],
           "recipeYield":["4","4 servings"],"prepTime":"PT15M","totalTime":"PT1H",
           "recipeCategory":"Dinner, Main course","recipeCuisine":["Tex-Mex"],"keywords":"easy, beef",
           "recipeIngredient":["1 lb ground beef","1 (14 oz) can kidney beans","2 tbsp chili powder"],
           "recipeInstructions":[
             {"@type":"HowToSection","name":"Chili","itemListElement":[
               {"@type":"HowToStep","text":"Brown the beef."},
               {"@type":"HowToStep","text":"Add beans &amp; spices; simmer."}]},
             {"@type":"HowToStep","text":"Serve over rice."}]}
        ]}
        </script></head><body></body></html>
        """;

    [Fact]
    public void Parses_schema_org_recipe_from_graph()
    {
        var r = RecipeImporter.Parse(Html, "https://example.com/chili")!;
        Assert.Equal("Weeknight Chili & Rice", r.Title);
        Assert.Equal("Cosy.", r.Description);
        Assert.Equal(4, r.Servings);
        Assert.Equal(15, r.PrepMinutes);
        Assert.Equal(45, r.CookMinutes);
        Assert.Equal("https://example.com/chili.jpg", r.ImageUrl);
        Assert.Equal(3, r.Ingredients.Count);
        Assert.Equal(["## Chili", "Brown the beef.", "Add beans & spices; simmer.", "Serve over rice."], r.Steps);
        Assert.Contains("tex-mex", r.Tags);
        Assert.Contains("easy", r.Tags);
        Assert.Contains("main course", r.Tags);
    }

    [Fact]
    public void Returns_null_without_recipe() => Assert.Null(RecipeImporter.Parse("<html><body>No recipe</body></html>", null));
}
