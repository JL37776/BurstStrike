namespace Game.Map
{
    /// <summary>
    /// 自动在场景(World)中创建并渲染示例地图（只渲染 Tanks layer），
    /// 避免手动在场景里挂脚本。
    /// 
    /// 工作方式：
    /// - 基于 RuntimeInitializeOnLoadMethod，在进入 Play 后自动创建一个 GameObject。
    /// - 在该物体上挂 TankMapRenderer，并直接调用 Render(map)。
    /// </summary>
    public static class WorldMapBootstrap
    {
         // 已迁移：地图渲染现在由 Game.World.World 在启动 LogicWorld 之前负责调用。
         // 这里保留文件以免破坏引用，但不再自动执行。
    }
}
