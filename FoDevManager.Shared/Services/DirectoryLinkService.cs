namespace FODevManager.Services
{
    public sealed class DirectoryLinkService : IDirectoryLinkService
    {
        public bool Exists(string path)
            => Directory.Exists(path);

        public void CreateSymbolicLink(string linkPath, string targetPath)
            => Directory.CreateSymbolicLink(linkPath, targetPath);

        public void Delete(string path, bool recursive = true)
            => Directory.Delete(path, recursive);

        public string? ResolveLinkTarget(string linkPath)
        {
            var target = new DirectoryInfo(linkPath).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName;
        }
    }
}
