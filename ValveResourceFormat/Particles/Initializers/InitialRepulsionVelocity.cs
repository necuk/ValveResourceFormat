namespace ValveResourceFormat.Particles.Initializers
{
    class InitialRepulsionVelocity : ParticleFunctionInitializer
    {
        private static readonly Vector3[] AxisDirections =
        [
            new(1f, 0f, 0f),
            new(-1f, 0f, 0f),
            new(0f, 1f, 0f),
            new(0f, -1f, 0f),
            new(0f, 0f, 1f),
            new(0f, 0f, -1f),
        ];

        private readonly Vector3 outputMin;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly int controlPointNumber;
        private readonly bool perParticle;
        private readonly bool translate;
        private readonly bool proportional;
        private readonly float traceLength = 64f;
        private readonly bool perParticleTraceRadius;
        private readonly bool inherit;
        private readonly int childControlPoint = -1;
        private readonly int childGroupId;

        private readonly Vector3[] directions = [.. AxisDirections];
        private readonly List<ParticleSystemState> matchingChildren = [];
        private bool batchOffsetPending = true;
        private Vector3 batchOffset;

        public InitialRepulsionVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            perParticle = parse.Boolean("m_bPerParticle", perParticle);
            translate = parse.Boolean("m_bTranslate", translate);
            proportional = parse.Boolean("m_bProportional", proportional);
            traceLength = parse.Float("m_flTraceLength", traceLength);
            perParticleTraceRadius = parse.Boolean("m_bPerParticleTR", perParticleTraceRadius);
            inherit = parse.Boolean("m_bInherit", inherit);
            childControlPoint = parse.Int32("m_nChildCP", childControlPoint);
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override void BeginInitializeBatch()
        {
            AxisDirections.CopyTo(directions);
            batchOffsetPending = true;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (perParticle)
            {
                var length = perParticleTraceRadius ? particle.Radius : traceLength;
                var (direction, strength) = TraceRepulsion(particle.Position, length, particleSystemState.Collision);
                var offset = ScaleOutput(direction * strength, strength);

                if (proportional)
                {
                    offset *= particle.Radius;
                }

                ApplyOffset(ref particle, offset, particleSystemState);
                return particle;
            }

            if (batchOffsetPending)
            {
                batchOffset = inherit ? InheritedOffset(particleSystemState) : ControlPointOffset(particleSystemState);
                batchOffsetPending = false;
            }

            if (proportional)
            {
                batchOffset *= particle.Radius;
            }

            ApplyOffset(ref particle, batchOffset, particleSystemState);
            return particle;
        }

        private Vector3 InheritedOffset(ParticleSystemState particleSystemState)
        {
            var direction = particleSystemState.GetControlPoint(controlPointNumber).Position;
            var strength = particleSystemState.GetControlPoint(controlPointNumber + 1).Position.X;

            return ScaleOutput(direction, strength);
        }

        private Vector3 ControlPointOffset(ParticleSystemState particleSystemState)
        {
            var origin = particleSystemState.GetControlPoint(controlPointNumber).Position;
            var (direction, strength) = TraceRepulsion(origin, traceLength, particleSystemState.Collision);
            var push = direction * strength;

            if (childControlPoint != -1 && particleSystemState.Data != null)
            {
                matchingChildren.Clear();
                particleSystemState.Data.CollectChildStatesInGroup(childGroupId, matchingChildren, includeDescendants: true);

                foreach (var child in matchingChildren)
                {
                    child.OverrideControlPoint(childControlPoint).Position = push;
                    child.OverrideControlPoint(childControlPoint + 1).Position = new Vector3(strength, 0f, 0f);
                }
            }

            return ScaleOutput(push, strength);
        }

        private (Vector3 Direction, float Strength) TraceRepulsion(Vector3 origin, float length, IParticleCollision collision)
        {
            var sum = Vector3.Zero;
            var strength = 0f;

            for (var i = 0; i < directions.Length; i++)
            {
                var direction = directions[i];
                collision.TraceRay(origin, origin + (direction * length), out _, out _, out var fraction);
                var open = 1f - fraction;
                var push = new Vector3(-direction.X * open, -direction.Y * open, -direction.Z * open);

                directions[i] = push;
                strength += open;
                sum += push;
            }

            if (sum == Vector3.Zero)
            {
                return (Vector3.UnitZ, 0f);
            }

            return (NormalizeVector(sum), strength);
        }

        private Vector3 ScaleOutput(Vector3 push, float strength)
            => (((outputMax - outputMin) * strength) + outputMin) * push;

        private void ApplyOffset(ref Particle particle, Vector3 offset, ParticleSystemState particleSystemState)
        {
            particle.Position += offset;

            if (translate)
            {
                return;
            }

            var frameTime = particleSystemState.Data?.CurrentFrameTime ?? 0f;
            if (frameTime > 0f)
            {
                particle.Velocity += offset / frameTime;
            }
        }

        private static Vector3 NormalizeVector(Vector3 value)
        {
            var length = MathF.Sqrt((value.Z * value.Z) + (value.Y * value.Y) + (value.X * value.X));

            if (length == 0f)
            {
                return Vector3.Zero;
            }

            if (length < 1e-17f || length > 1e17f)
            {
                var scale = 1.0 / Math.Sqrt((value.X * (double)value.X) + (value.Y * (double)value.Y) + (value.Z * (double)value.Z));
                return new Vector3((float)(value.X * scale), (float)(value.Y * scale), (float)(value.Z * scale));
            }

            var inverse = 1f / length;
            return new Vector3(value.X * inverse, value.Y * inverse, value.Z * inverse);
        }
    }
}
