namespace v2rayN.AutoSwitchCompanion;

internal static class V2rayNHost
{
    private static bool _initialized;

    public static string HostDirectory { get; private set; } = AppContext.BaseDirectory;

    public static bool TryInitialize(out string error)
    {
        error = string.Empty;
        if (_initialized)
        {
            return true;
        }

        var hostDirectory = FindHostDirectory(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(hostDirectory))
        {
            error = "Could not find v2rayN.exe. Put this companion next to v2rayN.exe or inside a direct child folder of the v2rayN directory.";
            return false;
        }

        HostDirectory = EnsureTrailingSeparator(hostDirectory);
        Environment.CurrentDirectory = HostDirectory;
        AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", HostDirectory);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveHostAssembly;
        AssemblyLoadContext.Default.Resolving += ResolveHostAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveHostNativeLibrary;

        _initialized = true;
        return true;
    }

    private static string? FindHostDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory != null && depth < 4; depth++, directory = directory.Parent)
        {
            var candidate = directory.FullName;
            if (File.Exists(Path.Combine(candidate, "v2rayN.exe")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Assembly? ResolveHostAssembly(object? sender, ResolveEventArgs args)
    {
        return ResolveHostAssembly(new AssemblyName(args.Name));
    }

    private static Assembly? ResolveHostAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        return ResolveHostAssembly(assemblyName);
    }

    private static Assembly? ResolveHostAssembly(AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            return null;
        }

        var assemblyPath = Path.Combine(HostDirectory, $"{assemblyName.Name}.dll");
        if (!File.Exists(assemblyPath))
        {
            return null;
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    private static nint ResolveHostNativeLibrary(Assembly assembly, string libraryName)
    {
        foreach (var candidate in GetNativeLibraryCandidates(libraryName))
        {
            var path = Path.Combine(HostDirectory, candidate);
            if (File.Exists(path))
            {
                return NativeLibrary.Load(path);
            }
        }

        return 0;
    }

    private static IEnumerable<string> GetNativeLibraryCandidates(string libraryName)
    {
        yield return libraryName;
        if (!libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{libraryName}.dll";
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : $"{fullPath}{Path.DirectorySeparatorChar}";
    }
}
