// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEditor;

namespace KINEMATION.ProceduralRecoilAnimationSystem.Editor
{
    [CustomEditor(typeof(RecoilAnimation))]
    public class RecoilAnimationEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor _recoilDataEditor;

        private void OnEnable()
        {
            UpdateRecoilDataEditor();
        }

        private void OnDisable()
        {
            if (_recoilDataEditor != null) DestroyImmediate(_recoilDataEditor);
        }

        private void UpdateRecoilDataEditor()
        {
            var recoilAnimation = target as RecoilAnimation;
            var recoilData = recoilAnimation == null ? null : recoilAnimation.recoilData;

            if (recoilData == null)
            {
                if (_recoilDataEditor != null) DestroyImmediate(_recoilDataEditor);
                _recoilDataEditor = null;
                return;
            }

            CreateCachedEditor(recoilData, typeof(RecoilAnimDataEditor), ref _recoilDataEditor);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            UpdateRecoilDataEditor();

            if (_recoilDataEditor == null) return;

            EditorGUILayout.Space();
            _recoilDataEditor.OnInspectorGUI();
        }
    }
}
