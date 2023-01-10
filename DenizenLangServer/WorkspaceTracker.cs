using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using SharpDenizenTools.ScriptAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DenizenLangServer
{
    public static class WorkspaceTracker
    {
        public static ConcurrentDictionary<string, ScriptChecker> Checkers = new();

        public static volatile ScriptingWorkspaceData WorkspaceData = null;

        public static long LastUpdate = 0;

        public static bool EverLoadedWorkspace = false;

        public static LockObject UpdateLock = new();

        public static string WorkspacePath;

        public static DiagnosticProvider Diagnostics;

        private static void AddInternal(Uri file, ScriptChecker checker)
        {
            string path = FixPath(file).Replace(WorkspacePath, "");
            foreach (ScriptContainerData script in checker.GeneratedWorkspace.Scripts.Values)
            {
                script.FileName = path;
            }
            Checkers[FixPath(file)] = checker;
        }

        public static void Replace(Uri file, ScriptChecker checker)
        {
            if (!ClientConfiguration.TrackFullWorkspace || WorkspacePath is null)
            {
                return;
            }
            AddInternal(file, checker);
            long index = ++LastUpdate;
            Task.Factory.StartNew(() => { UpdateWorkspaceData(index); });
        }

        public static string FixPath(Uri uri)
        {
            if (uri is null)
            {
                return null;
            }
            return Uri.UnescapeDataString(uri.ToString()["file:///".Length..]);
        }

        public static Uri PathToUri(string path)
        {
            return new("file:///" + Uri.EscapeDataString(path));
        }

        public static void UpdateWorkspaceData(long updateCounter)
        {
            lock (UpdateLock)
            {
                try
                {
                    if (updateCounter < LastUpdate)
                    {
                        return;
                    }
                    if (!EverLoadedWorkspace)
                    {
                        Console.Error.WriteLine($"Doing first-time scan of workspace: {WorkspacePath}");
                        EverLoadedWorkspace = true;
                        foreach (string file in Directory.EnumerateFiles(WorkspacePath, "*.dsc", SearchOption.AllDirectories))
                        {
                            string path = Path.GetFullPath(file);
                            Uri uri = new(path);
                            string fixedPath = FixPath(uri);
                            if (!Checkers.ContainsKey(fixedPath))
                            {
                                ScriptChecker checker = new(File.ReadAllText(path));
                                if (Checkers.TryAdd(fixedPath, checker))
                                {
                                    checker.Run();
                                    AddInternal(uri, checker);
                                }
                            }
                        }
                        Console.Error.WriteLine($"Have {Checkers.Count} files loaded and initially scanned");
                        ScriptingWorkspaceData genData = new();
                        KeyValuePair<string, ScriptChecker>[] copyCheckers = Checkers.ToArray();
                        foreach ((_, ScriptChecker checker) in copyCheckers)
                        {
                            genData.MergeIn(checker.GeneratedWorkspace);
                        }
                        foreach ((string path, _) in copyCheckers)
                        {
                            ScriptChecker checker = new(File.ReadAllText(path))
                            {
                                SurroundingWorkspace = genData
                            };
                            checker.Run();
                            AddInternal(PathToUri(path), checker);
                            Diagnostics.PublishCheckerResults(PathToUri(path), checker);
                        }
                        WorkspaceData = genData;
                        Console.Error.WriteLine($"Have {Checkers.Count} files fully scanned and ready");
                    }
                    ScriptingWorkspaceData NewData = new();
                    foreach ((string path, ScriptChecker checker) in Checkers.ToArray())
                    {
                        Console.Error.WriteLine($"Merge {path} in dataset {string.Join(',', checker.GeneratedWorkspace.Scripts.Keys)}");
                        NewData.MergeIn(checker.GeneratedWorkspace);
                    }
                    WorkspaceData = NewData;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to update workspace data set: {ex}");
                }
            }
        }
    }
}
