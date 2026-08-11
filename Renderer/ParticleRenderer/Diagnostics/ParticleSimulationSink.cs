namespace ValveResourceFormat.Renderer.Particles.Diagnostics
{
    /// <summary>
    /// The phase of one simulation step a recorded function ran in. The order below is the order the
    /// step runs them in, and it is what makes "the last function that wrote this field" answerable.
    /// </summary>
    internal enum ParticleFunctionPhase
    {
        /// <summary>A <c>C_OP_</c> pre-emission operator, which reads and writes system state only.</summary>
        PreEmission,

        /// <summary>A <c>C_INIT_</c> initializer, run once per particle at spawn.</summary>
        Initializer,

        /// <summary>An emitter, whose spawns are the initializer records that precede it.</summary>
        Emitter,

        /// <summary>A <c>C_OP_</c> per-particle operator.</summary>
        Operator,

        /// <summary>A constraint pass.</summary>
        Constraint,
    }

    /// <summary>
    /// Receives the simulation as it runs, one system at a time. Every call is made by
    /// <see cref="ParticleRenderer"/> from inside the step it describes, so an implementation that
    /// reads <see cref="ParticleRenderer.CurrentParticles"/> sees exactly the state that function left
    /// behind — which is what lets a divergence be attributed to the function that caused it rather
    /// than to the frame it surfaced in.
    /// </summary>
    internal abstract class ParticleSimulationSink
    {
        /// <summary>
        /// Called once per simulated step per system, after the age has advanced and before any
        /// function has run.
        /// </summary>
        /// <param name="system">The system being stepped.</param>
        /// <param name="frameTime">The step length, in seconds.</param>
        public abstract void StepBegin(ParticleRenderer system, float frameTime);

        /// <summary>
        /// Called after one function of the step has run. The particle state at this moment is the
        /// function's output.
        /// </summary>
        /// <param name="system">The system being stepped.</param>
        /// <param name="phase">Which of the step's walks the function belongs to.</param>
        /// <param name="index">The function's position in that walk, as authored.</param>
        /// <param name="className">The function's <c>_class</c>.</param>
        public abstract void FunctionRan(ParticleRenderer system, ParticleFunctionPhase phase, int index, string className);

        /// <summary>
        /// Called after one initializer has run for one spawning particle, with the particle as it
        /// stood before the call. Initializers run inside emission rather than over the live array, so
        /// they are the one phase whose input cannot be recovered from the previous record.
        /// </summary>
        /// <param name="system">The system the particle is spawning into.</param>
        /// <param name="index">The initializer's position in the initializer list, as authored.</param>
        /// <param name="className">The initializer's <c>_class</c>.</param>
        /// <param name="before">The particle as the initializer received it.</param>
        /// <param name="after">The particle as the initializer left it.</param>
        public abstract void InitializerRan(ParticleRenderer system, int index, string className, in Particle before, in Particle after);

        /// <summary>
        /// Called at the end of a step, after dead particles have been pruned, with the state the
        /// frame would be drawn from.
        /// </summary>
        /// <param name="system">The system that was stepped.</param>
        public abstract void StepEnd(ParticleRenderer system);
    }
}
