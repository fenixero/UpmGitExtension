using System;
using UnityEngine.UIElements;
using UnityEditor.PackageManager.UI.Internal;

namespace Coffee.UpmGitExtension
{
    internal static class VisualElementExtension
    {
        public static void OverwriteCallback(this Button button, Action action)
        {
            button.RemoveManipulator(button.clickable);
            button.clickable = new Clickable(action);
            button.AddManipulator(button.clickable);
        }

        public static VisualElement GetRoot(this VisualElement element)
        {
            while (element != null && element.parent != null)
            {
                element = element.parent;
            }

            return element;
        }
#if UNITY_2021_3_OR_NEWER
        public static bool GetRootTrue(this VisualElement element)
        {
            int i = 1;
            while (element != null && element.parent != null)
            {
                element = element.parent;
                i++;
            }
            if (element.Q<PackageDetails>() != null && element.Q<TemplateContainer>() != null && element.Q("toolbarAddMenu") != null && element.Q<PackageManagerToolbar>() != null && element.Q<VisualElement>("refreshButton") != null)
                return true;
            else
                return false;
        }
#endif
    }
}

