using System;

namespace Game.Map.Terrain
{
    /// <summary>
    /// 笔刷形状。
    /// </summary>
    public enum BrushShape
    {
        /// <summary>圆形笔刷（按欧氏距离）</summary>
        Circle,

        /// <summary>方形笔刷（按切比雪夫距离）</summary>
        Square,
    }

    /// <summary>
    /// 笔刷衰减曲线：决定从中心到边缘的强度衰减方式。
    /// </summary>
    public enum BrushFalloff
    {
        /// <summary>无衰减，整个笔刷范围内强度相同</summary>
        Constant,

        /// <summary>线性衰减</summary>
        Linear,

        /// <summary>平滑衰减 (smoothstep)</summary>
        Smooth,
    }

    /// <summary>
    /// 笔刷参数，描述一次面操作的影响范围与衰减。
    /// 所有坐标均为顶点空间（整数格子坐标）。
    /// </summary>
    public struct BrushParams
    {
        /// <summary>笔刷中心 X（顶点坐标）</summary>
        public int CenterX;

        /// <summary>笔刷中心 Z（顶点坐标）</summary>
        public int CenterZ;

        /// <summary>笔刷半径（顶点数）</summary>
        public int Radius;

        /// <summary>笔刷形状</summary>
        public BrushShape Shape;

        /// <summary>衰减曲线</summary>
        public BrushFalloff Falloff;

        /// <summary>操作强度 (0~1)，与衰减相乘后作用到最终值</summary>
        public float Strength;

        /// <summary>创建一个默认笔刷（圆形、平滑衰减、半径5、强度1）</summary>
        public static BrushParams Default(int cx, int cz)
        {
            return new BrushParams
            {
                CenterX  = cx,
                CenterZ  = cz,
                Radius   = 5,
                Shape    = BrushShape.Circle,
                Falloff  = BrushFalloff.Smooth,
                Strength = 1f,
            };
        }
    }
}
