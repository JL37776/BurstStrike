using UnityEngine;
using Game.Map.Terrain;

namespace Game.Map
{
    /// <summary>
    /// MonoBehaviour 桥接层：持有 <see cref="MapEditor"/> 实例，
    /// 管理其生命周期并暴露给 UI 层和其他运行时组件。
    /// </summary>
    public sealed class MapEditorBridge : MonoBehaviour
    {
        /// <summary>核心编辑器实例（纯 C# 类）</summary>
        public MapEditor Editor { get; private set; }

        /// <summary>当前打开的文件路径（null = 未保存的新地图）</summary>
        public string CurrentFilePath { get; set; }

        private void Awake()
        {
            Editor = new MapEditor();
        }

        private void OnDestroy()
        {
            Editor?.Dispose();
            Editor = null;
        }
    }
}
