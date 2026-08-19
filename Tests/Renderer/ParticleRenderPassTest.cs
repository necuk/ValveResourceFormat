using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Renderer
{
    public class ParticleRenderPassTest
    {
        [Test]
        public async Task EffectsWaterRendererDoesNotLeakIntoTranslucentPass()
        {
            var effectsPasses = GetRenderPasses(onlyRenderInEffectsWaterPass: true);
            var ordinaryPasses = GetRenderPasses(onlyRenderInEffectsWaterPass: false);

            using (Assert.Multiple())
            {
                await Assert.That(effectsPasses & CustomRenderPasses.Translucent).IsEqualTo(CustomRenderPasses.None);
                await Assert.That(effectsPasses & CustomRenderPasses.WaterEffects).IsEqualTo(CustomRenderPasses.WaterEffects);
                await Assert.That(ordinaryPasses & CustomRenderPasses.Translucent).IsEqualTo(CustomRenderPasses.Translucent);
            }
        }

        private static CustomRenderPasses GetRenderPasses(bool onlyRenderInEffectsWaterPass)
        {
            var renderer = KVObject.Collection();
            renderer.Add("_class", "C_OP_RenderSound");
            renderer.Add("m_bOnlyRenderInEffectsWaterPass", onlyRenderInEffectsWaterPass);

            var renderers = KVObject.Array();
            renderers.Add(renderer);

            var definition = KVObject.Collection();
            definition.Add("_class", "CParticleSystemDefinition");
            definition.Add("m_Renderers", renderers);

            using var fileLoader = new GameFileLoader(null, null);
            using var rendererContext = new RendererContext(fileLoader, NullLogger.Instance);
            using var scene = new Scene(rendererContext);
            var node = new ParticleSceneNode(scene, ParticleSystem.Create(definition));
            var passes = node.RenderPasses;
            node.Delete();
            return passes;
        }
    }
}
