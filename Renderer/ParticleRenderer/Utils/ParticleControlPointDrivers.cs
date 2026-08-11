using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// Reads the control point configurations an effect is authored with. An effect is played under
    /// one of them, and the configuration is where several constants its operators depend on actually
    /// live, so a system started without one is not the same effect.
    /// </summary>
    internal static class ParticleControlPointDrivers
    {
        /// <summary>
        /// Seeds the control points carrying a literal driver offset under the configuration the
        /// effect plays outside the editor. Points a driver leaves at zero are skipped: control point
        /// 0 is the effect's placement and comes from its owner, and the rest we would only be writing
        /// their default back.
        /// </summary>
        /// <param name="particleSystem">The effect whose configurations are read.</param>
        /// <param name="getControlPoint">Resolves a control point number to the point to write.</param>
        public static void ApplyRuntimeValues(ParticleSystem particleSystem, Func<int, ControlPoint> getControlPoint)
        {
            ArgumentNullException.ThrowIfNull(particleSystem);
            ArgumentNullException.ThrowIfNull(getControlPoint);

            var drivers = ChooseRuntimeConfiguration(particleSystem)?.GetArray("m_drivers");

            if (drivers == null)
            {
                return;
            }

            foreach (var driver in drivers)
            {
                var controlPoint = driver.ContainsKey("m_iControlPoint") ? driver.GetInt32Property("m_iControlPoint") : 0;

                if (controlPoint == 0)
                {
                    continue;
                }

                var offset = ReadVector(driver, "m_vecOffset");

                if (offset == Vector3.Zero)
                {
                    continue;
                }

                getControlPoint(controlPoint).Position = offset;
            }
        }

        /// <summary>
        /// The configuration the effect plays under outside the editor: viewmodel effects carry a
        /// first-person one, everything else plays under <c>game</c>. Any non-preview configuration
        /// beats nothing.
        /// </summary>
        public static KVObject? ChooseRuntimeConfiguration(ParticleSystem particleSystem)
        {
            ArgumentNullException.ThrowIfNull(particleSystem);

            var configurations = particleSystem.Data.GetArray("m_controlPointConfigurations");

            if (configurations == null)
            {
                return null;
            }

            var viewModelEffect = particleSystem.Data.GetStringProperty("m_nViewModelEffect") == "INHERITABLE_BOOL_TRUE";
            var wantedConfiguration = viewModelEffect ? "fps_view" : "game";

            KVObject? chosen = null;

            foreach (var configuration in configurations)
            {
                var name = configuration.GetStringProperty("m_name");

                if (string.Equals(name, wantedConfiguration, StringComparison.OrdinalIgnoreCase))
                {
                    return configuration;
                }

                if (chosen == null && !string.Equals(name, "preview", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = configuration;
                }
            }

            return chosen;
        }

        /// <summary>
        /// Reads a driver's vector. Driver vectors can have null components (e.g.
        /// <c>m_angOffset = [null, null, null]</c>), which the plain conversion throws on;
        /// <c>GetFloatArray</c> maps null elements to 0.
        /// </summary>
        /// <param name="driver">The driver block.</param>
        /// <param name="key">The key holding the vector.</param>
        public static Vector3 ReadVector(KVObject driver, string key)
        {
            ArgumentNullException.ThrowIfNull(driver);

            var components = driver.GetFloatArray(key);

            return components is { Length: >= 3 }
                ? new Vector3(components[0], components[1], components[2])
                : Vector3.Zero;
        }
    }
}
