using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class LibrarySearchTests
{
    [Theory]
    [InlineData("The Witcher 3", "", true)]
    [InlineData("The Witcher 3", "   ", true)]
    [InlineData("The Witcher 3", "witcher", true)]
    [InlineData("The Witcher 3", "WITCHER 3", true)]
    [InlineData("The Witcher 3", "3 witcher", true)]
    [InlineData("The Witcher 3", "witcher 4", false)]
    [InlineData("Ёлки-палки", "елки", true)]
    [InlineData("Елки-палки", "ёлки", true)]
    [InlineData("Сталкер 2", "сталкер", true)]
    [InlineData("Сталкер 2", "metro", false)]
    public void Matches(string name, string query, bool expected)
    {
        Assert.Equal(expected, LibrarySearch.Matches(name, query));
    }
}
