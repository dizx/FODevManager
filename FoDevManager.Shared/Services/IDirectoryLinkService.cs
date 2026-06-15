namespace FODevManager.Services
{
    public interface IDirectoryLinkService
    {
        bool Exists(string path);

        void CreateSymbolicLink(string linkPath, string targetPath);

        void Delete(string path, bool recursive = true);

        string? ResolveLinkTarget(string linkPath);
    }
}
