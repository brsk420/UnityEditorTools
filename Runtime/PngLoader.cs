using System.IO;
using UnityEngine;

#if UNITY_EDITOR
namespace _Brsk420.Runtime
{
    // Lives in the runtime assembly so both DownscalePreview and the editor tools can use it.
    public static class PngLoader
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// Loads a PNG into <paramref name="texture"/>. Returns false (and logs) instead of silently
        /// leaving Unity's 8x8 "?" placeholder in the texture when decoding fails.
        /// Text chunks are stripped first: Photoshop can embed tens of MB of XMP history into
        /// iTXt, which exceeds libpng's chunk size limit and makes LoadImage fail.
        /// </summary>
        public static bool TryLoad(Texture2D texture, byte[] bytes, string path)
        {
            if (texture.LoadImage(StripTextChunks(bytes)))
            {
                return true;
            }

            Debug.LogError($"PngLoader: failed to decode {path}, skipped.");
            return false;
        }

        public static byte[] StripTextChunks(byte[] bytes)
        {
            if (bytes.Length < Signature.Length || !HasSignature(bytes))
            {
                return bytes;
            }

            using var output = new MemoryStream(bytes.Length);
            output.Write(bytes, 0, Signature.Length);

            int offset = Signature.Length;

            while (offset + 12 <= bytes.Length)
            {
                int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
                int chunkSize = 12 + length;

                if (length < 0 || offset + chunkSize > bytes.Length)
                {
                    // Malformed; let LoadImage decide what to do with the original data.
                    return bytes;
                }

                string type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);

                if (type != "iTXt" && type != "tEXt" && type != "zTXt")
                {
                    output.Write(bytes, offset, chunkSize);
                }

                offset += chunkSize;

                if (type == "IEND")
                {
                    break;
                }
            }

            return output.ToArray();
        }

        private static bool HasSignature(byte[] bytes)
        {
            for (int i = 0; i < Signature.Length; i++)
            {
                if (bytes[i] != Signature[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
