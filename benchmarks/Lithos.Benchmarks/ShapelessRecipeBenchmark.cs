using Vintagestory.API.Common;

namespace Lithos.Benchmarks;

internal sealed class ShapelessRecipeBenchmark : IBenchmarkCase
{
    private readonly RecipeHarness recipe = new();
    private readonly ItemSlot[] slots = new ItemSlot[9];
    private readonly IRecipeIngredient?[] ingredients = new IRecipeIngredient?[9];
    private readonly List<ItemStack> stacks = new(9);

    public ShapelessRecipeBenchmark()
    {
        var exactIngredient = new CraftingRecipeIngredient
        {
            MatchingType = EnumRecipeMatchType.Exact
        };
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = new DummySlot();
            if (index % 2 == 0) ingredients[index] = exactIngredient;
        }
    }

    public string Name => "crafting-shapeless-recipes";

    public string Description => "Merges an empty 3x3 crafting grid and filters exact recipe ingredients.";

    public void Validate()
    {
        var firstItem = new BenchmarkItem { ItemId = 1 };
        var secondItem = new BenchmarkItem { ItemId = 2 };
        var firstStack = new ItemStack(firstItem, 2);
        var mergeSlots = new ItemSlot[]
        {
            new DummySlot(firstStack),
            new DummySlot(new ItemStack(firstItem, 3)),
            new DummySlot(new ItemStack(secondItem, 4)),
            new DummySlot()
        };
        var mergedStacks = new List<ItemStack>();

        Ensure(recipe.Merge(mergeSlots, mergedStacks) == 2, "equivalent stacks were not merged");
        Ensure(mergedStacks[0].StackSize == 5, "merged stack size changed");
        Ensure(mergedStacks[1].StackSize == 4, "distinct stack size changed");
        Ensure(firstStack.StackSize == 2, "an input stack was mutated");
        Ensure(!ReferenceEquals(mergedStacks[0], firstStack), "an input stack was not cloned");

        var exactIngredient = new CraftingRecipeIngredient
        {
            MatchingType = EnumRecipeMatchType.Exact
        };
        Ensure(
            recipe.MatchWildcards(new List<ItemStack>(), [null, exactIngredient]),
            "null and exact ingredients were not ignored");

        var wildcardIngredient = new CraftingRecipeIngredient
        {
            MatchingType = EnumRecipeMatchType.Wildcard
        };
        Ensure(
            !recipe.MatchWildcards(new List<ItemStack>(), [wildcardIngredient]),
            "a missing wildcard ingredient was ignored");

        Ensure(Run(1) == 1, "measurement workload checksum changed");
    }

    public int Run(int iterations)
    {
        var checksum = 0;
        for (var index = 0; index < iterations; index++)
        {
            stacks.Clear();
            checksum += recipe.Merge(slots, stacks);
            if (recipe.MatchWildcards(stacks, ingredients)) checksum++;
        }

        return checksum;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private sealed class BenchmarkItem : Item
    {
        public override bool Satisfies(ItemStack thisStack, ItemStack otherStack)
        {
            return thisStack.Class == otherStack.Class && thisStack.Id == otherStack.Id;
        }
    }

    private sealed class RecipeHarness : RecipeBase
    {
        private readonly CraftingRecipeIngredient output = new();

        public override IEnumerable<IRecipeIngredient> RecipeIngredients => [];

        public override IRecipeOutput RecipeOutput => output;

        public int Merge(ItemSlot[] suppliedSlots, List<ItemStack> suppliedStacks)
        {
            MergeStacks(suppliedSlots, suppliedStacks);
            return suppliedStacks.Count;
        }

        public bool MatchWildcards(List<ItemStack> suppliedStacks, IRecipeIngredient?[] recipeIngredients)
        {
            return MatchWildcardIngredients(suppliedStacks, recipeIngredients);
        }

        public override bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            return true;
        }

        public override RecipeBase Clone()
        {
            return new RecipeHarness();
        }
    }
}
