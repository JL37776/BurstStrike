#if UNITY_ENGINE || UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Game.Scripts.Fixed
{
    public static class FixedRendering
    {
        public static Vector3[] ToUnityVertices(FixedVector3[] fixedVerts)
        {
            if (fixedVerts == null) return null;
            var outv = new Vector3[fixedVerts.Length];
            for (int i = 0; i < fixedVerts.Length; i++) outv[i] = fixedVerts[i].ToUnity();
            return outv;
        }

        public static float[] ToFloatArray(FixedVector3[] fixedVerts)
        {
            if (fixedVerts == null) return null;
            var arr = new float[fixedVerts.Length * 3];
            for (int i = 0; i < fixedVerts.Length; i++)
            {
                arr[i * 3] = fixedVerts[i].x.ToFloat();
                arr[i * 3 + 1] = fixedVerts[i].y.ToFloat();
                arr[i * 3 + 2] = fixedVerts[i].z.ToFloat();
            }
            return arr;
        }
    }
}
#endif
