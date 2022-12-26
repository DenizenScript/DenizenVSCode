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
        public static ConcurrentDictionary<Uri, ScriptChecker> Checkers = new();

        public static volatile ScriptingWorkspaceData WorkspaceData = null;

        public static long LastUpdate = 0;

        public static bool EverLoadedWorkspace = false;

        public static LockObject UpdateLock = new();

        public static string WorkspacePath;

        public static DiagnosticProvider Diagnostics;

        public static void Replace(Uri file, ScriptChecker checker)
        {
            if (!ClientConfiguration.TrackFullWorkspace)
            {
                return;
            }
            Checkers[file] = checker;
            long index = ++LastUpdate;
            Task.Factory.StartNew(() => { UpdateWorkspaceData(index); });
        }

        public static string FixPath(Uri uri)
        {
            return Uri.UnescapeDataString(uri.ToString()["file:///".Length..]);
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
                            if (!Checkers.ContainsKey(uri))
                            {
                                ScriptChecker checker = new(File.ReadAllText(path));
                                if (Checkers.TryAdd(uri, checker))
                                {
                                    checker.Run();
                                }
                            }
                        }
                        Console.Error.WriteLine($"Have {Checkers.Count} files loaded and initially scanned");
                        ScriptingWorkspaceData genData = new();
                        KeyValuePair<Uri, ScriptChecker>[] copyCheckers = Checkers.ToArray();
                        foreach ((_, ScriptChecker checker) in copyCheckers)
                        {
                            genData.MergeIn(checker.GeneratedWorkspace);
                        }
                        foreach ((Uri path, _) in copyCheckers)
                        {
                            ScriptChecker checker = new(File.ReadAllText(FixPath(path)))
                            {
                                SurroundingWorkspace = genData
                            };
                            Checkers[path] = checker;
                            checker.Run();
                            Diagnostics.PublishCheckerResults(path, checker);
                        }
                        WorkspaceData = genData;
                        Console.Error.WriteLine($"Have {Checkers.Count} files fully scanned and ready");
                    }
                    ScriptingWorkspaceData NewData = new();
                    foreach ((_, ScriptChecker checker) in Checkers.ToArray())
                    {
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
