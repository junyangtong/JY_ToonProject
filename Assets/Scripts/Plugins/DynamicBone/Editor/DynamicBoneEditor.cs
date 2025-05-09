using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(DynamicBone))]
public class DynamicBoneEditor : Editor
{
    SerializedProperty m_Root;
    SerializedProperty m_Roots;
    
    void OnEnable()
    {
        m_Root = serializedObject.FindProperty("m_Root");
        m_Roots = serializedObject.FindProperty("m_Roots");
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        DynamicBone db = target as DynamicBone;
        
        EditorGUILayout.PropertyField(m_Root);
        EditorGUILayout.PropertyField(m_Roots, true);
        
        if (GUILayout.Button("添加选中物体到Roots"))
        {
            Transform[] selectedTransforms = Selection.transforms;
            if (selectedTransforms.Length > 0)
            {
                Undo.RecordObject(db, "Add Selected To Roots");
                
                if (db.m_Roots == null)
                    db.m_Roots = new List<Transform>();
                    
                foreach (Transform t in selectedTransforms)
                {
                    if (!db.m_Roots.Contains(t))
                    {
                        db.m_Roots.Add(t);
                    }
                }
                
                EditorUtility.SetDirty(db);
            }
        }
        
        EditorGUILayout.Space(10);

        Editor.DrawPropertiesExcluding(serializedObject, "m_Root", "m_Roots");
        
        serializedObject.ApplyModifiedProperties();
    }
}
