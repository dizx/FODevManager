using System.Security.Cryptography;
using System.Xml.Linq;

namespace FODevManager.Operations;

public static class ModelPathSafety
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool SamePath(string left, string right) => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsWithin(string path, string parent) => SamePath(path, parent)
        || Normalize(path).StartsWith(Normalize(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void RequireNewDestination(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) || new DirectoryInfo(path).LinkTarget != null)
            throw new InvalidOperationException($"Model destination already exists: '{path}'. Use add-from-path for existing models");
        RejectLinkedAncestors(path);
    }

    public static void RejectLinkedAncestors(string path)
    {
        for (var current = new DirectoryInfo(Normalize(path)); current != null; current = current.Parent)
            if (current.LinkTarget != null)
                throw new InvalidOperationException($"Model path traverses a directory link: '{current.FullName}'");
    }

    public static IReadOnlyDictionary<string, string> ValidateConversion(string source, string destination, string modelName)
    {
        RequireNewDestination(destination);
        RejectLinkedAncestors(source);
        if (IsWithin(destination, source) || IsWithin(source, destination))
            throw new InvalidOperationException("Installed source and conversion destination must be disjoint");
        var descriptor = Path.Combine(source, "Descriptor", modelName + ".xml");
        if (!File.Exists(descriptor)) throw new InvalidOperationException("Installed source model descriptor is missing");
        var document = XDocument.Load(descriptor);
        if (document.Root?.Name.LocalName != "AxModelInfo"
            || !string.Equals(document.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value, modelName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installed descriptor does not match the requested model name");
        return Snapshot(source);
    }

    public static IReadOnlyDictionary<string, string> Snapshot(string path)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Installed conversion cannot traverse reparse points");
                if (entry is DirectoryInfo) pending.Push(entry.FullName);
                else
                {
                    using var stream = File.OpenRead(entry.FullName);
                    files.Add(Path.GetRelativePath(path, entry.FullName), Convert.ToHexString(SHA256.HashData(stream)));
                }
            }
        }
        return files;
    }

    public static void VerifyCopy(string source, string destination, IReadOnlyDictionary<string, string> expected)
    {
        RejectLinkedAncestors(source);
        RejectLinkedAncestors(destination);
        foreach (var snapshot in new[] { Snapshot(source), Snapshot(destination) })
            if (snapshot.Count != expected.Count || expected.Any(p => !snapshot.TryGetValue(p.Key, out var digest) || digest != p.Value))
                throw new InvalidOperationException("Installed model copy verification failed; original deployment has been retained");
    }
}
