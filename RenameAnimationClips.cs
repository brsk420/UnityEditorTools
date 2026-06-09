using UnityEngine;
using UnityEditor;

namespace _Brsk420.EditorTools
{
    public class RenameAnimationClips : MonoBehaviour
    {
        [MenuItem("_BrskTools/Rename Animation Clips")]
        static void RenameClips()
        {
            // Указываем путь к папке, где находятся ваши FBX файлы
            string folderPath = "Assets/BIV/Art/Avatar/Male/Animations";

            // Проходим по каждому файлу в папке
            string[] fbxFiles = System.IO.Directory.GetFiles(folderPath, "*.fbx", System.IO.SearchOption.AllDirectories);

            foreach (string fbxFile in fbxFiles)
            {
                string assetPath = fbxFile.Replace("\\", "/");
                ModelImporter modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;

                if (modelImporter != null)
                {
                    // Получаем все анимационные клипы из FBX файла
                    ModelImporterClipAnimation[] clipAnimations = modelImporter.clipAnimations;

                    for (int i = 0; i < clipAnimations.Length; i++)
                    {
                        // Удаляем "SK_Body_F_001a@SK_Body_F_001a" и добавляем префикс "SK_Body_F_001@"
                        string newName = clipAnimations[i].name.Replace("_", "");
                        clipAnimations[i].name = newName;
                    }

                    // Применяем изменения
                    modelImporter.clipAnimations = clipAnimations;
                    modelImporter.SaveAndReimport();
                }
            }

            Debug.Log("Animation clips renamed successfully.");
        }
    }
}
