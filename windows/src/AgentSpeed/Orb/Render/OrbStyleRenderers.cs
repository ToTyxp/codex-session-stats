using AgentSpeed.Orb.Render.Styles;
using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Orb.Render;

/// <summary>Creates the renderer (with its model) of a style.</summary>
internal static class OrbStyleRenderers
{
    public static IOrbStyleRenderer Create(OrbStyle style, OrbScene scene, uint seed) => style switch
    {
        OrbStyle.Needle => new NeedleRenderer(scene, seed),
        OrbStyle.Minimal => new MinimalRenderer(scene),
        OrbStyle.Liquid => new LiquidRenderer(scene, seed),
        OrbStyle.Reactor => new ReactorRenderer(scene, seed),
        _ => new LedRenderer(scene),
    };
}
