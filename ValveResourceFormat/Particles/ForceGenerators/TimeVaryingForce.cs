namespace ValveResourceFormat.Particles.ForceGenerators;

class TimeVaryingForce : ParticleFunctionForceGenerator
{
    private readonly float startLerpTime;
    private readonly float endLerpTime = 10f;
    private readonly Vector3 startingForce;
    private readonly Vector3 endingForce;

    public TimeVaryingForce(ParticleDefinitionParser parse) : base(parse)
    {
        startLerpTime = parse.Float("m_flStartLerpTime", startLerpTime);
        endLerpTime = parse.Float("m_flEndLerpTime", endLerpTime);
        startingForce = parse.Vector3("m_StartingForce", startingForce);
        endingForce = parse.Vector3("m_EndingForce", endingForce);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var start = startingForce * strength;
        var end = endingForce * strength;
        var scale = 1f / (endLerpTime - startLerpTime);

        foreach (ref var particle in particles.Current)
        {
            var fraction = MathUtils.Saturate((particle.Age - startLerpTime) * scale);

            particle.ForceAccumulator += Vector3.Lerp(start, end, fraction);
        }
    }
}
