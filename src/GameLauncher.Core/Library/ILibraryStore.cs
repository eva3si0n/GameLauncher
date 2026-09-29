namespace GameLauncher.Core.Library;

public interface ILibraryStore
{
    LibraryData Load();

    void Save(LibraryData data);
}
