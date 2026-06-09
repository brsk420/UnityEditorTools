using UnityEngine;
using UnityEditor;

namespace _Brsk420.EditorTools
{
    public class ColorReset : MonoBehaviour
    {
        [MenuItem("CONTEXT/Material/Set Colors to White")]
        static void SetColorsToWhite(MenuCommand command)
        {
            Material material = command.context as Material;

            if (material != null)
            {
                Undo.RecordObject(material, "Set Colors to White");

                Shader shader = material.shader;
                int propertyCount = ShaderUtil.GetPropertyCount(shader);

                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.Color)
                    {
                        string propertyName = ShaderUtil.GetPropertyName(shader, i);
                        material.SetColor(propertyName, Color.white);
                    }
                }

                EditorUtility.SetDirty(material);
            }
        }
    }
}