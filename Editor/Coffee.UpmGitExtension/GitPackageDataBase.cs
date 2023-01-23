using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Reflection;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.Text.RegularExpressions;
#if UNITY_2021_1_OR_NEWER
using UnityEditor.PackageManager.UI.Internal;
#else
using UnityEditor.PackageManager.UI;
#endif

namespace Coffee.UpmGitExtension
{
    [Serializable]
    internal class FetchResult : ISerializationCallbackReceiver
    {
        public string id;
        public string url;
        public int hash;
        public UpmPackageVersionEx[] versions;

        public override int GetHashCode()
        {
            return hash;
        }

        public override bool Equals(object obj)
        {
            return (obj as FetchResult)?.hash == hash;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            versions = versions
                .Where(v => v.isValid)
                .ToArray();
        }
    }

    /// <summary>
    /// Database of packages installed via Git
    /// </summary>
    //[FilePath("GitPackageDatabase.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal class GitPackageDatabase : ScriptableSingleton<GitPackageDatabase>
    {
        //################################
        // Public Members.
        //################################
        // public static event Action OnChangedPackages;

        public static void Install(string packageId)
        {
            _upmClient.AddByUrl(packageId);
        }

        public static void Uninstall(string packageId)
        {
            var i = packageId.IndexOf('@');
            var packageName = packageId.Substring(0, i);
            _upmClient.RemoveByName(packageName);
        }

        public static IEnumerable<UpmPackage> GetUpmPackages()
        {
            return _packageDatabase.allPackages.OfType<UpmPackage>();
        }

        public static IEnumerable<UpmPackage> GetInstalledGitPackages()
        {
            return GetUpmPackages()
                .Where(p => p.GetInstalledVersion()?.HasTag(PackageTag.Git) == true);
        }

        public static string[] GetCachedRepositoryUrls()
        {
            return _resultCaches.Select(x => x.url).ToArray();
        }

        /// <summary>
        /// Fetch the available git package versions.
        /// </summary>
        public static void Fetch(string url, Action<int> callback = null)
        {
            const string kFetchPackagesJs = "Packages/com.coffee.upm-git-extension/Editor/Commands/fetch-packages.js";
#if UNITY_EDITOR_WIN
            var node = Path.Combine(EditorApplication.applicationContentsPath, "Tools/nodejs/node.exe").Replace('/', '\\');
#else
            var node = Path.Combine(EditorApplication.applicationContentsPath, "Tools/nodejs/bin/node");
#endif

            var args = $"\"{Path.GetFullPath(kFetchPackagesJs)}\" {url}";
            var p = new NativeProgram(node, args);
            p._process.StartInfo.WorkingDirectory = _workingDirectory;
            p.Start((_, __) =>
            {
                var exitCode = p._process.ExitCode;
                if (exitCode != 0)
                {
                    Debug.LogError(p.GetAllOutput());
                }
                callback?.Invoke(p._process.ExitCode);
            });
        }

        internal static IPackage GetPackage(IPackageVersion packageVersion)
        {
            return _packageDatabase.GetPackage(packageVersion);
        }

        internal static IPackage GetPackage(string packageName)
        {
            return _packageDatabase.GetPackage(packageName);
        }

        internal static IPackageVersion GetPackageVersion(string packageUniqueId, string versionUniqueId)
        {
            IPackage package;
            IPackageVersion version;
            _packageDatabase.GetPackageAndVersion(packageUniqueId, versionUniqueId, out package, out version);
            return version;
        }

        /// <summary>
        /// Update available versions for git packages.
        /// </summary>
        public static void Fetch()
        {

            GetInstalledGitPackages()
#if UNITY_2021_3_OR_NEWER
                .Select(p => PackageExtensions.GetSourceUrlFromID(p?.versions?.primary?.uniqueId))
#else
                .Select(p => p?.versions?.primary?.packageInfo?.GetSourceUrl())
#endif
                .Where(url => !string.IsNullOrEmpty(url))
                .Concat(GetCachedRepositoryUrls())
                .Distinct()
                .ForEach(url => Fetch(url));
        }

        public static void OpenCacheDirectory()
        {
            if (Directory.Exists(_workingDirectory))
                EditorUtility.RevealInFinder(_workingDirectory);
        }

        public static void ClearCache()
        {
            _resultCaches.Clear();

            if (Directory.Exists(_workingDirectory))
                Directory.Delete(_workingDirectory, true);

            UnityEngine.Debug.Log("[GitPackageDatabase] Clear Cache");
            WatchResultJson();
        }

        public static void ResetCacheTime()
        {
            isPaused = true;
            var resultDir = Path.GetFullPath(_resultsDir);
            foreach (var file in Directory.GetFiles(resultDir, "*.json"))
            {
                File.SetLastWriteTime(file, DateTime.Now.AddMinutes(-10));
            }
            isPaused = false;
        }
#if UNITY_2021_3_OR_NEWER
        public static IEnumerable<UpmPackageVersion> GetAvailablePackageVersions(string packageId = null, string repoUrl = null, bool preRelease = false)
#else
	    public static IEnumerable<UpmPackageVersionEx> GetAvailablePackageVersions(string packageId = null, string repoUrl = null, bool preRelease = false)
#endif
        {
            return _resultCaches
                .SelectMany(r => r.versions)
                .Where(v => v.isValid && (preRelease || _enablePreReleasePackages || !v.IsPreRelease())
                            && (string.IsNullOrEmpty(packageId) || v.packageUniqueId == packageId)
                            && (string.IsNullOrEmpty(repoUrl) || v.uniqueId.Contains(repoUrl))
                );
        }

        //################################
        // Private Members.
        //################################
        private static string _workingDirectory => InternalEditorUtility.unityPreferencesFolder + "/GitPackageDatabase";
        //private static string _workingDirectory => "Library/GitPackageDatabase";
        private static string _serializeVersion => "2.0.0";
        private static string _resultsDir => _workingDirectory + "/Results-" + _serializeVersion;
        private static FileSystemWatcher _watcher;
        private static bool isPaused;
        private static readonly HashSet<FetchResult> _resultCaches = new HashSet<FetchResult>();
        private static PackageManagerProjectSettings _settings => ScriptableSingleton<PackageManagerProjectSettings>.instance;
#if UNITY_2020_2_OR_NEWER
        internal static UpmClient _upmClient => ScriptableSingleton<ServicesContainer>.instance.Resolve<UpmClient>();
        internal static PackageDatabase _packageDatabase => ScriptableSingleton<ServicesContainer>.instance.Resolve<PackageDatabase>();
#if UNITY_2021_3_OR_NEWER
        internal static Dictionary<string, UnityEditor.PackageManager.PackageInfo> _packages;
#endif
        internal static PageManager _pageManager => ScriptableSingleton<ServicesContainer>.instance.Resolve<PageManager>();
#else
        internal static IUpmClient _upmClient => UpmClient.instance;
        internal static IPackageDatabase _packageDatabase => PackageDatabase.instance;
        internal static IPageManager _pageManager => PageManager.instance;
#endif

#if UNITY_2021_1_OR_NEWER
        private static bool _enablePreReleasePackages => _settings.enablePreReleasePackages;
#if UNITY_2021_3_OR_NEWER
        static internal void AddPackageInfo(UpmPackageVersionEx pack)
        {
            if (_packages == null)
                _packages = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
            bool tryadd = _packages.TryAdd(pack.uniqueId, pack.packageInfo);
            //if (tryadd)
            //    Debug.Log("11 - " + pack.uniqueId);
        }

        static internal UnityEditor.PackageManager.PackageInfo GetPackageInfo(string id)
        {
            if(_packages==null)
                _packages = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
            UnityEditor.PackageManager.PackageInfo[] packs = UnityEditor.PackageManager.PackageInfo.GetAll();
            foreach (var pack1 in packs)
            {
                bool tryadd = _packages.TryAdd(pack1.packageId, pack1);
                //if (tryadd)
                //    Debug.Log("22 - " + pack1.packageId);
            }
            if (_packages.ContainsKey(id))
                return _packages[id];
            else
                return null;
        }
#endif
#else
        private static bool _enablePreReleasePackages => _settings.enablePreviewPackages;
#endif

        private static void RequestUpdateGitPackageVersions()
        {
            EditorApplication.delayCall -= UpdateGitPackageVersions;
            EditorApplication.delayCall += UpdateGitPackageVersions;
        }

        private static void UpdateGitPackageVersions()
        {
            var installedIds = new HashSet<string>(
                GetUpmPackages()
                    .Where(p => p.GetInstalledVersion() != null)
                    .Select(p => p.uniqueId)
            );

            var packages = GetAvailablePackageVersions()
                .ToLookup(v => v.packageUniqueId)
                .Select(versions =>
                {
                    var isInstalled = installedIds.Contains(versions.Key);
                    if (isInstalled)
                    {
                        // Git mode: Register all installable package versions.
                        var upmPackage = _packageDatabase.GetPackage(versions.Key) as UpmPackage;
#if UNITY_2021_3_OR_NEWER
                        var installedVersion = upmPackage.versions.installed as UpmPackageVersion;
                        UnityEditor.PackageManager.PackageInfo packageInfo = GetPackageInfo(installedVersion.uniqueId);
#else
                        var installedVersion = upmPackage.versions.installed as UpmPackageVersion;
                        UnityEditor.PackageManager.PackageInfo packageInfo = installedVersion.packageInfo;
#endif
                        if (packageInfo.source != UnityEditor.PackageManager.PackageSource.Git)
                            return upmPackage;

                        // Unlock.
                        installedVersion.UnlockVersion();
						
#if UNITY_2021_3_OR_NEWER
                        var newVersions = new[] { installedVersion }
#else
                        var newVersions = new[] { new UpmPackageVersionEx(installedVersion) }
#endif
                                .Concat(versions.Where(v => v.uniqueId != installedVersion.uniqueId))
#if UNITY_2021_3_OR_NEWER
                                .OrderBy(v => v.version)
#else
	                            .OrderBy(v => v.semVersion)
#endif
                                .ThenBy(v => v.isInstalled)
                                .ToArray();

#if UNITY_2021_3_OR_NEWER
                        upmPackage.UpdateVersions(newVersions, 0);
#else
                        upmPackage.UpdateVersions(newVersions);
#endif

                        return upmPackage;
                    }
                    else
                    {
                        // Registory mode: Register as installable package.
                        var upmPackage = new UpmPackage(versions.Key + " (git)", true, PackageType.ScopedRegistry);
#if UNITY_2021_3_OR_NEWER
                        upmPackage.UpdateVersions(versions.OrderBy(v => v.version), 0);
#else
                        upmPackage.UpdateVersions(versions.OrderBy(v => v.version));
#endif
                        upmPackage.Set("m_Type", PackageType.MainNotUnity | PackageType.Installable);
                        return upmPackage;
                    }
                })
                .Where(p => p != null);

            EditorApplication.delayCall += () => _packageDatabase.Call("OnPackagesChanged", packages);

#if UNITY_2021_1_OR_NEWER
            if (!_settings.seeAllPackageVersions)
            {
                _settings.seeAllPackageVersions = true;
                _settings.Save();
            }
#endif
        }

        private static void OnResultFileCreated(string file)
        {
            if (isPaused || string.IsNullOrEmpty(file) || Path.GetExtension(file) != ".json" || !File.Exists(file))
                return;

            try
            {
                var text = File.ReadAllText(file, System.Text.Encoding.UTF8);
                var result = JsonUtility.FromJson<FetchResult>(text);

                _resultCaches.RemoveWhere(r => r.url == result.url);
                _resultCaches.Add(result);
                RequestUpdateGitPackageVersions();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [InitializeOnLoadMethod]
        private static void WatchResultJson()
        {
            _resultCaches.Clear();

#if !UNITY_EDITOR_WIN
            Environment.SetEnvironmentVariable("MONO_MANAGED_WATCHER", "enabled");
#endif
            var resultDir = Path.GetFullPath(_resultsDir);
            if (!Directory.Exists(resultDir))
                Directory.CreateDirectory(resultDir);

            GitPackageDatabase.RequestUpdateGitPackageVersions();
            foreach (var file in Directory.GetFiles(resultDir, "*.json"))
                EditorApplication.delayCall += () => OnResultFileCreated(Path.Combine(resultDir, file));

            _watcher?.Dispose();
            _watcher = new FileSystemWatcher()
            {
                Path = resultDir,
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };

            _watcher.Created += (s, e) => EditorApplication.delayCall += () => OnResultFileCreated(e.FullPath);

            _pageManager.onRefreshOperationStart += Fetch;
            _pageManager.onRefreshOperationFinish += Fetch;

            _upmClient.onPackagesChanged += _ => RequestUpdateGitPackageVersions();

#if UNITY_2021_1_OR_NEWER
            _settings.onEnablePreReleasePackagesChanged += _ => RequestUpdateGitPackageVersions();
#elif UNITY_2020_2_OR_NEWER
            _settings.onEnablePreviewPackagesChanged += _ => RequestUpdateGitPackageVersions();
#else
            _settings.onEnablePreviewPackageChanged += _ => RequestUpdateGitPackageVersions();
#endif
        }
    }
}