namespace ValveResourceFormat.Renderer.Particles.Diagnostics
{
    /// <summary>
    /// What a harness attaches to one system tree so it can be driven outside the viewer. A system
    /// built without one behaves exactly as it does in the scene; the field is null there and every
    /// hook below folds away to a null check.
    /// </summary>
    /// <remarks>
    /// The harness is handed to the root system's constructor and inherited by every child, so one
    /// instance covers a whole tree.
    /// </remarks>
    internal sealed class ParticleSimulationHarness
    {
        /// <summary>
        /// Whether to leave the renderers unbuilt. Every renderer allocates GL objects in its
        /// constructor, so a system that is only simulated must skip them to run with no context.
        /// </summary>
        public bool SkipRenderers { get; init; }

        /// <summary>Where the per-step and per-function records go, when the harness records them.</summary>
        public ParticleSimulationSink? Sink { get; init; }
    }
}
