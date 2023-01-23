using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Scripting.ScriptCompilation;
using System.Reflection;
#if UNITY_2021_1_OR_NEWER
using UnityEditor.PackageManager.UI.Internal;
#else
using UnityEditor.PackageManager.UI;
#endif

using UnityEditor.PackageManager;
namespace Coffee.UpmGitExtension
{
    [Serializable]
    internal class UpmPackageVersionEx : UpmPackageVersion
    {
        private static readonly Regex regex = new Regex("^(\\d +)\\.(\\d +)\\.(\\d +)(.*)$", RegexOptions.Compiled);
        private static SemVersion? unityVersion;

        public UpmPackageVersionEx(UnityEditor.PackageManager.PackageInfo packageInfo, bool isInstalled, bool isUnityPackage) : base(packageInfo, isInstalled, isUnityPackage)
        {
#if UNITY_2021_3_OR_NEWER
            this.packageInfo = packageInfo;
#endif	
        }

        public UpmPackageVersionEx(UnityEditor.PackageManager.PackageInfo packageInfo, bool isInstalled, SemVersion? version, string displayName, bool isUnityPackage) : base(packageInfo, isInstalled, version, displayName, isUnityPackage)
        {
#if UNITY_2021_3_OR_NEWER
            this.packageInfo = packageInfo;
#endif	
        }

#if UNITY_2021_3_OR_NEWER
        public UpmPackageVersionEx(UpmPackageVersion packageVersion, UnityEditor.PackageManager.PackageInfo packageInfo) : base(packageInfo, packageVersion.isInstalled,packageVersion.version,packageInfo.displayName, packageVersion.isUnityPackage)
        {
            this.packageInfo = packageInfo;
#else
	    public UpmPackageVersionEx(UpmPackageVersion packageVersion) : base(packageVersion.packageInfo, packageVersion.isInstalled, packageVersion.isUnityPackage)
		{	
#endif	
            m_MinimumUnityVersion = UnityVersionToSemver(Application.unityVersion).ToString();
            OnAfterDeserialize();
        }

#if UNITY_2021_3_OR_NEWER
        [SerializeField] private UnityEditor.PackageManager.PackageInfo m_PackageInfo;
        public UnityEditor.PackageManager.PackageInfo packageInfo { get => m_PackageInfo; set => m_PackageInfo = value; }
#endif
        public string fullVersionString { get; private set; }
        public SemVersion semVersion { get; private set; }

        [SerializeField]
        private string m_MinimumUnityVersion;

        public bool isValid { get; private set; }

        private static SemVersion UnityVersionToSemver(string version)
        {
            return SemVersionParser.Parse(regex.Replace(version, "$1.$2.$3+$4"));
        }

        public bool IsPreRelease()
        {
            return semVersion.Major == 0 || !string.IsNullOrEmpty(semVersion.Prerelease);
        }

        private void UpdateTag()
        {
            PackageTag tag = PackageTag.Git | PackageTag.Installable | PackageTag.Removable;
            if (IsPreRelease())
            {
#if UNITY_2021_1_OR_NEWER
                tag |= PackageTag.PreRelease;
#else
                tag |= PackageTag.Preview;
#endif
            }
            else
                tag |= PackageTag.Release;

            this.Set("m_Tag", tag);
        }


        public override void OnAfterDeserialize()
        {
#if UNITY_2021_3_OR_NEWER
            var m_UpmErrors = typeof(UpmPackageVersion).GetField("m_UpmErrors", BindingFlags.NonPublic | BindingFlags.Instance).GetValue((UpmPackageVersion)this);
            if (m_UpmErrors == null)
            {
                Error[] arr = Array.Empty<Error>();
                typeof(UpmPackageVersion).GetField("m_UpmErrors", BindingFlags.NonPublic | BindingFlags.Instance).SetValue((UpmPackageVersion)this, arr);
            }
            GitPackageDatabase.AddPackageInfo(this);
#endif
            base.OnAfterDeserialize();

            semVersion = m_Version ?? new SemVersion();
            var revision = packageInfo?.git?.revision ?? "";
            if (!revision.Contains(m_VersionString) && 0 < revision.Length)
            {
                fullVersionString = $"{m_Version} ({revision})";
            }
            else
            {
                fullVersionString = m_Version.ToString();
            }

            try
            {
                if (!unityVersion.HasValue)
                    unityVersion = UnityVersionToSemver(Application.unityVersion);
                var supportedUnityVersion = UnityVersionToSemver(m_MinimumUnityVersion);

                isValid = supportedUnityVersion <= unityVersion.Value;

                if (HasTag(PackageTag.Git))
                    UpdateTag();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

        }
    }
}